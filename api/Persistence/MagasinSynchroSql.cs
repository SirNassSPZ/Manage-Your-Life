using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Migrations;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Api.Persistence;

/// <summary>
/// Implémentation d'<see cref="IMagasinSynchro"/> sur base relationnelle (adaptateur, règle 4).
/// Source de vérité : les tables typées §9 ; la lecture reconstitue l'état depuis la colonne
/// <c>payload</c> canonique (fidélité aller-retour, D-012). Le même code tourne sur Azure SQL
/// (production) et SQLite (tests, bases locales) ; seule la récupération du <c>server_seq</c>
/// auto-incrémenté du journal diffère selon le dialecte.
/// </summary>
public sealed class MagasinSynchroSql(Func<DbConnection> fabriqueConnexion, DialecteSql dialecte)
    : IMagasinSynchro
{
    private DbConnection? _connexionTx;
    private DbTransaction? _tx;

    // ----- Métadonnées : table + colonnes NON NULL peuplées depuis le payload (D-012) -----

    private static readonly IReadOnlyDictionary<EntiteSynchro, string> Tables =
        new Dictionary<EntiteSynchro, string>
        {
            [EntiteSynchro.Element] = "elements",
            [EntiteSynchro.Categorie] = "categories",
            [EntiteSynchro.Projet] = "projets",
            [EntiteSynchro.Budget] = "budgets",
            [EntiteSynchro.PieceJointe] = "attachments",
            [EntiteSynchro.Reglage] = "settings",
        };

    private static readonly IReadOnlyDictionary<EntiteSynchro, string[]> ColonnesTypees =
        new Dictionary<EntiteSynchro, string[]>
        {
            [EntiteSynchro.Element] =
                ["type", "titre", "statut", "journee_entiere", "date_approximative", "est_obligatoire",
                 "date_creation", "date_modification", "appareil_source", "version", "server_seq", "supprime"],
            [EntiteSynchro.Categorie] =
                ["nom", "couleur", "origine", "date_creation", "date_modification", "appareil_source",
                 "version", "server_seq", "supprime"],
            [EntiteSynchro.Projet] =
                ["nom", "couleur", "statut", "date_creation", "date_modification", "appareil_source",
                 "version", "server_seq", "supprime"],
            [EntiteSynchro.Budget] =
                ["nom", "couleur", "montant_periode_centimes", "periode", "statut", "date_creation",
                 "date_modification", "appareil_source", "version", "server_seq", "supprime"],
            [EntiteSynchro.PieceJointe] =
                ["element_id", "nom_fichier", "taille_octets", "blob_path", "confirme", "date_creation",
                 "date_modification", "appareil_source", "version", "server_seq", "supprime"],
        };

    private enum Kind { Text, Guid, Date, Int, Long, Bool }

    private static Kind KindDe(string colonne) => colonne switch
    {
        "id" or "element_id" or "appareil_source" or "appareil_id" or "change_id"
            or "entite_id" or "projet_id" or "budget_id" or "categorie_id" => Kind.Guid,
        "date_creation" or "date_modification" or "date_suppression" or "recu_le"
            or "purge_le" or "date_enregistrement" => Kind.Date,
        "version" or "score_points" or "ordre_manuel" => Kind.Int,
        "montant_centimes" or "montant_periode_centimes" or "taille_octets" or "server_seq" => Kind.Long,
        "journee_entiere" or "date_approximative" or "est_obligatoire" or "supprime" or "confirme" => Kind.Bool,
        _ => Kind.Text,
    };

    // ----- État courant des entités -----

    public EtatEntite? Obtenir(EntiteSynchro entite, Guid id)
        => Executer(entite == EntiteSynchro.Reglage
            ? "SELECT valeur FROM settings WHERE cle = @cle"
            : $"SELECT payload FROM {Tables[entite]} WHERE id = @id",
            (cmd) =>
            {
                if (entite == EntiteSynchro.Reglage)
                    Param(cmd, "@cle", ReglageSolde.Cle);
                else
                    Param(cmd, "@id", id);
                using var lecteur = cmd.ExecuteReader();
                return lecteur.Read() && !lecteur.IsDBNull(0)
                    ? EtatDepuisPayload(entite, lecteur.GetString(0))
                    : null;
            });

    public void Ecrire(EtatEntite etat)
    {
        var payload = JsonDocument.Parse(etat.PayloadCanonique).RootElement;
        if (etat.Entite == EntiteSynchro.Reglage)
        {
            EcrireReglage(etat, payload);
            return;
        }

        var colonnes = ColonnesTypees[etat.Entite];
        var table = Tables[etat.Entite];
        var toutes = new[] { "id" }.Concat(colonnes).Append("payload").ToArray();

        // UPSERT portable (SQLite + SQL Server) : UPDATE, puis INSERT si aucune ligne touchée.
        var setClause = string.Join(", ", colonnes.Append("payload").Select(c => $"{c} = @{c}"));
        var maj = ExecuterNonQuery($"UPDATE {table} SET {setClause} WHERE id = @id",
            cmd => PeuplerColonnes(cmd, etat, colonnes, payload));
        if (maj == 0)
        {
            var listeCol = string.Join(", ", toutes);
            var listeVal = string.Join(", ", toutes.Select(c => "@" + c));
            ExecuterNonQuery($"INSERT INTO {table} ({listeCol}) VALUES ({listeVal})",
                cmd => PeuplerColonnes(cmd, etat, colonnes, payload));
        }
    }

    private void EcrireReglage(EtatEntite etat, JsonElement payload)
    {
        void Peupler(DbCommand cmd)
        {
            Param(cmd, "@cle", ReglageSolde.Cle);
            Param(cmd, "@valeur", etat.PayloadCanonique);
            Param(cmd, "@date_modification", ValeurColonne("date_modification", payload));
            Param(cmd, "@version", ValeurColonne("version", payload));
            Param(cmd, "@server_seq", ValeurColonne("server_seq", payload));
            Param(cmd, "@supprime", ValeurColonne("supprime", payload));
        }

        var maj = ExecuterNonQuery(
            "UPDATE settings SET valeur = @valeur, date_modification = @date_modification, " +
            "version = @version, server_seq = @server_seq, supprime = @supprime WHERE cle = @cle", Peupler);
        if (maj == 0)
            ExecuterNonQuery(
                "INSERT INTO settings (cle, valeur, date_modification, version, server_seq, supprime) " +
                "VALUES (@cle, @valeur, @date_modification, @version, @server_seq, @supprime)", Peupler);
    }

    private void PeuplerColonnes(DbCommand cmd, EtatEntite etat, string[] colonnes, JsonElement payload)
    {
        Param(cmd, "@id", etat.Id);
        foreach (var c in colonnes)
            Param(cmd, "@" + c, ValeurColonne(c, payload));
        Param(cmd, "@payload", etat.PayloadCanonique);
    }

    public void SupprimerEtat(EntiteSynchro entite, Guid id)
    {
        if (entite == EntiteSynchro.Reglage)
            ExecuterNonQuery("DELETE FROM settings WHERE cle = @cle", cmd => Param(cmd, "@cle", ReglageSolde.Cle));
        else
            ExecuterNonQuery($"DELETE FROM {Tables[entite]} WHERE id = @id", cmd => Param(cmd, "@id", id));
    }

    public IReadOnlyList<EtatEntite> ModifiesDepuis(long depuis, int limite)
    {
        // Union ordonnée par server_seq global sur toutes les entités synchronisées (pull, §6.2).
        var parties = new List<string>();
        foreach (var (entite, table) in Tables)
        {
            var (col, nom) = entite == EntiteSynchro.Reglage ? ("valeur", "reglage") : ("payload", NomEntite(entite));
            parties.Add($"SELECT '{nom}' AS entite, {col} AS payload, server_seq FROM {table}");
        }
        var requete = $"SELECT entite, payload FROM ({string.Join(" UNION ALL ", parties)}) t " +
                      "WHERE server_seq > @depuis ORDER BY server_seq";
        return Executer(requete, cmd =>
        {
            Param(cmd, "@depuis", depuis);
            using var lecteur = cmd.ExecuteReader();
            var resultat = new List<EtatEntite>();
            while (lecteur.Read() && resultat.Count < limite)
                resultat.Add(EtatDepuisPayload(EntiteDepuis(lecteur.GetString(0)), lecteur.GetString(1)));
            return resultat;
        });
    }

    public IReadOnlyList<EtatEntite> EnumererEtats(EntiteSynchro entite)
        => Executer(entite == EntiteSynchro.Reglage
            ? "SELECT valeur FROM settings"
            : $"SELECT payload FROM {Tables[entite]}",
            cmd =>
            {
                using var lecteur = cmd.ExecuteReader();
                var resultat = new List<EtatEntite>();
                while (lecteur.Read())
                    if (!lecteur.IsDBNull(0))
                        resultat.Add(EtatDepuisPayload(entite, lecteur.GetString(0)));
                return resultat;
            });

    public IReadOnlyList<EtatEntite> TachesAFaireDuProjet(Guid projetId)
        => EnumererEtats(EntiteSynchro.Element)
            .Where(e => !e.Supprime)
            .Where(e =>
            {
                var el = SerialisationCanonique.Deserialiser<Element>(e.PayloadCanonique);
                return el.Type == TypeElement.Tache && el.Statut == StatutElement.AFaire && el.ProjetId == projetId;
            })
            .OrderBy(e => e.ServerSeq)
            .ToList();

    public IReadOnlyList<EtatEntite> PiecesJointesDeLElement(Guid elementId)
        => EnumererEtats(EntiteSynchro.PieceJointe)
            .Where(e => SerialisationCanonique.Deserialiser<PieceJointe>(e.PayloadCanonique).ElementId == elementId)
            .OrderBy(e => e.ServerSeq)
            .ToList();

    // ----- Journal des changements (§6.2.4) -----

    public EntreeJournal? JournalParChangeId(Guid changeId)
        => Executer(
            "SELECT server_seq, entite, element_id, payload, appareil_id, resultat, recu_le " +
            "FROM change_log WHERE change_id = @change_id",
            cmd =>
            {
                Param(cmd, "@change_id", changeId);
                using var lecteur = cmd.ExecuteReader();
                if (!lecteur.Read())
                    return (EntreeJournal?)null;
                return new EntreeJournal(
                    LireLong(lecteur, 0), changeId, EntiteDepuis(lecteur.GetString(1)),
                    LireGuid(lecteur, 2), lecteur.GetString(3), LireGuid(lecteur, 4),
                    ResultatDepuis(lecteur.GetString(5)), LireDate(lecteur, 6));
            });

    public long AjouterJournal(EntreeJournal entree)
    {
        var sql = dialecte == DialecteSql.Sqlite
            ? """
              INSERT INTO change_log (change_id, entite, element_id, payload, appareil_id, resultat, recu_le)
              VALUES (@change_id, @entite, @element_id, @payload, @appareil_id, @resultat, @recu_le);
              SELECT last_insert_rowid();
              """
            : """
              INSERT INTO change_log (change_id, entite, element_id, payload, appareil_id, resultat, recu_le)
              OUTPUT INSERTED.server_seq
              VALUES (@change_id, @entite, @element_id, @payload, @appareil_id, @resultat, @recu_le);
              """;

        return Executer(sql, cmd =>
        {
            Param(cmd, "@change_id", entree.ChangeId);
            Param(cmd, "@entite", NomEntite(entree.Entite));
            Param(cmd, "@element_id", entree.EntiteId);
            Param(cmd, "@payload", entree.Payload);
            Param(cmd, "@appareil_id", entree.AppareilId);
            Param(cmd, "@resultat", NomResultat(entree.Resultat));
            Param(cmd, "@recu_le", entree.RecuLe.UtcDateTime);
            var id = cmd.ExecuteScalar();
            return Convert.ToInt64(id, CultureInfo.InvariantCulture);
        });
    }

    public long SeqCourante
        => Executer("SELECT COALESCE(MAX(server_seq), 0) FROM change_log",
            cmd => Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture));

    public void CaviarderJournal(EntiteSynchro entite, Guid id, string marqueur)
        => ExecuterNonQuery(
            "UPDATE change_log SET payload = @m WHERE entite = @e AND element_id = @id AND payload <> @m",
            cmd =>
            {
                Param(cmd, "@m", marqueur);
                Param(cmd, "@e", NomEntite(entite));
                Param(cmd, "@id", id);
            });

    // ----- Pierres tombales de purge (§5.6, D-010) -----

    public PierreTombale? ObtenirTombale(EntiteSynchro entite, Guid id)
        => Executer(
            "SELECT server_seq, change_id, appareil_id, purge_le FROM purges WHERE entite = @e AND entite_id = @id",
            cmd =>
            {
                Param(cmd, "@e", NomEntite(entite));
                Param(cmd, "@id", id);
                using var lecteur = cmd.ExecuteReader();
                return lecteur.Read()
                    ? new PierreTombale(entite, id, LireLong(lecteur, 0), LireGuid(lecteur, 1),
                        LireGuid(lecteur, 2), LireDate(lecteur, 3))
                    : (PierreTombale?)null;
            });

    public void AjouterTombale(PierreTombale tombale)
        => ExecuterNonQuery(
            "INSERT INTO purges (entite, entite_id, server_seq, change_id, appareil_id, purge_le) " +
            "VALUES (@entite, @entite_id, @server_seq, @change_id, @appareil_id, @purge_le)",
            cmd =>
            {
                Param(cmd, "@entite", NomEntite(tombale.Entite));
                Param(cmd, "@entite_id", tombale.Id);
                Param(cmd, "@server_seq", tombale.ServerSeq);
                Param(cmd, "@change_id", tombale.ChangeId);
                Param(cmd, "@appareil_id", tombale.AppareilId);
                Param(cmd, "@purge_le", tombale.PurgeLe.UtcDateTime);
            });

    public IReadOnlyList<PierreTombale> PurgesDepuis(long depuis, int limite)
        => Executer(
            "SELECT entite, entite_id, server_seq, change_id, appareil_id, purge_le " +
            "FROM purges WHERE server_seq > @depuis ORDER BY server_seq",
            cmd =>
            {
                Param(cmd, "@depuis", depuis);
                using var lecteur = cmd.ExecuteReader();
                var resultat = new List<PierreTombale>();
                while (lecteur.Read() && resultat.Count < limite)
                    resultat.Add(new PierreTombale(
                        EntiteDepuis(lecteur.GetString(0)), LireGuid(lecteur, 1), LireLong(lecteur, 2),
                        LireGuid(lecteur, 3), LireGuid(lecteur, 4), LireDate(lecteur, 5)));
                return resultat;
            });

    // ----- Atomicité (§6.2.2) -----

    public void DansTransaction(Action action)
    {
        if (_connexionTx is not null)
        {
            action(); // déjà dans une transaction — participation directe
            return;
        }
        var connexion = fabriqueConnexion();
        var ouverteIci = connexion.State != ConnectionState.Open;
        if (ouverteIci)
            connexion.Open();
        var tx = connexion.BeginTransaction();
        _connexionTx = connexion;
        _tx = tx;
        try
        {
            action();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        finally
        {
            _connexionTx = null;
            _tx = null;
            tx.Dispose();
            if (ouverteIci)
                connexion.Dispose();
        }
    }

    // ----- Exécution : réutilise la connexion de transaction, sinon ouvre à la demande -----

    private T Executer<T>(string sql, Func<DbCommand, T> op)
    {
        if (_connexionTx is not null)
            return AvecCommande(_connexionTx, _tx, sql, op);
        var connexion = fabriqueConnexion();
        var ouverteIci = connexion.State != ConnectionState.Open;
        if (ouverteIci)
            connexion.Open();
        try
        {
            return AvecCommande(connexion, null, sql, op);
        }
        finally
        {
            if (ouverteIci)
                connexion.Dispose();
        }
    }

    private static T AvecCommande<T>(DbConnection connexion, DbTransaction? tx, string sql, Func<DbCommand, T> op)
    {
        using var cmd = connexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return op(cmd);
    }

    private int ExecuterNonQuery(string sql, Action<DbCommand> preparer)
        => Executer(sql, cmd => { preparer(cmd); return cmd.ExecuteNonQuery(); });

    // ----- Conversions payload → paramètres et lecture typée cross-dialecte -----

    private static void Param(DbCommand cmd, string nom, object? valeur)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = nom;
        p.Value = valeur ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static object ValeurColonne(string colonne, JsonElement payload)
    {
        if (!payload.TryGetProperty(colonne, out var p) || p.ValueKind == JsonValueKind.Null)
            return DBNull.Value;
        return KindDe(colonne) switch
        {
            Kind.Guid => Guid.Parse(p.GetString()!),
            Kind.Date => DateTimeOffset.Parse(p.GetString()!, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).UtcDateTime,
            Kind.Int => p.GetInt32(),
            Kind.Long => p.GetInt64(),
            Kind.Bool => p.GetBoolean() ? 1 : 0,
            _ => p.GetString()!,
        };
    }

    private static EtatEntite EtatDepuisPayload(EntiteSynchro entite, string payload)
    {
        var e = JsonDocument.Parse(payload).RootElement;
        return new EtatEntite(
            entite,
            Guid.Parse(e.GetProperty("id").GetString()!),
            e.GetProperty("version").GetInt32(),
            e.GetProperty("date_modification").GetDateTimeOffset(),
            e.TryGetProperty("supprime", out var s) && s.GetBoolean(),
            e.GetProperty("server_seq").GetInt64(),
            payload);
    }

    private static Guid LireGuid(DbDataReader r, int i)
        => r.GetValue(i) is Guid g ? g : Guid.Parse((string)r.GetValue(i));

    private static long LireLong(DbDataReader r, int i)
        => Convert.ToInt64(r.GetValue(i), CultureInfo.InvariantCulture);

    private static DateTimeOffset LireDate(DbDataReader r, int i)
        => r.GetValue(i) switch
        {
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
            string t => DateTimeOffset.Parse(t, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            var v => throw new InvalidOperationException($"Type de date inattendu : {v?.GetType()}"),
        };

    // ----- Noms d'énumérations (miroir du JSON canonique, D-007) -----

    private static string NomEntite(EntiteSynchro e) => e switch
    {
        EntiteSynchro.Element => "element",
        EntiteSynchro.Categorie => "categorie",
        EntiteSynchro.Projet => "projet",
        EntiteSynchro.Budget => "budget",
        EntiteSynchro.PieceJointe => "piece_jointe",
        EntiteSynchro.Reglage => "reglage",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    private static EntiteSynchro EntiteDepuis(string s) => s switch
    {
        "element" => EntiteSynchro.Element,
        "categorie" => EntiteSynchro.Categorie,
        "projet" => EntiteSynchro.Projet,
        "budget" => EntiteSynchro.Budget,
        "piece_jointe" => EntiteSynchro.PieceJointe,
        "reglage" => EntiteSynchro.Reglage,
        _ => throw new ArgumentException($"Entité inconnue : {s}"),
    };

    private static string NomResultat(ResultatChangement r) => r switch
    {
        ResultatChangement.Applique => "applique",
        ResultatChangement.PerdantArchive => "perdant_archive",
        ResultatChangement.Purge => "purge",
        ResultatChangement.RefusePurge => "refuse_purge",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    private static ResultatChangement ResultatDepuis(string s) => s switch
    {
        "applique" => ResultatChangement.Applique,
        "perdant_archive" => ResultatChangement.PerdantArchive,
        "purge" => ResultatChangement.Purge,
        "refuse_purge" => ResultatChangement.RefusePurge,
        _ => throw new ArgumentException($"Résultat inconnu : {s}"),
    };
}
