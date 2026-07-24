using System.Globalization;
using System.Text.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Microsoft.Data.Sqlite;

namespace DeuxiemeCerveau.App.Local;

/// <summary>
/// Dépôt local de l'app (adaptateur de persistance, règle 4) — source de lecture de l'UI et support
/// des filets de synchro (§6). Les entités sont stockées par leur <c>payload</c> canonique (fidélité
/// aller-retour, D-007) ; les colonnes typées §9 restent peuplées pour la conformité au schéma partagé.
/// Porte aussi l'<c>outbox</c> persistante (§6.2), le curseur de pull et le réglage (solde §3.4).
/// Aucune génération de <c>server_seq</c> côté client (posé par le serveur, §6.2) — ici toujours 0
/// tant qu'une entité n'a pas été tirée du serveur.
/// </summary>
public sealed class DepotLocal(SqliteConnection connexion)
{
    // ----- Métadonnées : table + colonnes typées NON NULL peuplées depuis le payload (schéma §9) -----

    private static readonly IReadOnlyDictionary<EntiteSynchro, string> Tables =
        new Dictionary<EntiteSynchro, string>
        {
            [EntiteSynchro.Element] = "elements",
            [EntiteSynchro.Categorie] = "categories",
            [EntiteSynchro.Projet] = "projets",
            [EntiteSynchro.Budget] = "budgets",
            [EntiteSynchro.PieceJointe] = "attachments",
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

    // ----- Écriture / lecture des entités -----

    /// <summary>Écrit (upsert) l'état d'une entité depuis son payload canonique. Le réglage va dans <c>settings</c>.</summary>
    public void Ecrire(EtatEntite etat)
    {
        if (etat.Entite == EntiteSynchro.Reglage)
        {
            EcrireReglage(etat);
            return;
        }

        var payload = JsonDocument.Parse(etat.PayloadCanonique).RootElement;
        var colonnes = ColonnesTypees[etat.Entite];
        var table = Tables[etat.Entite];
        var toutes = new[] { "id" }.Concat(colonnes).Append("payload").ToArray();

        // UPSERT portable : UPDATE, puis INSERT si aucune ligne touchée (miroir du serveur).
        var set = string.Join(", ", colonnes.Append("payload").Select(c => $"{c} = @{c}"));
        var maj = ExecuterNonQuery($"UPDATE {table} SET {set} WHERE id = @id",
            cmd => Peupler(cmd, etat, colonnes, payload));
        if (maj == 0)
            ExecuterNonQuery(
                $"INSERT INTO {table} ({string.Join(", ", toutes)}) VALUES ({string.Join(", ", toutes.Select(c => "@" + c))})",
                cmd => Peupler(cmd, etat, colonnes, payload));
    }

    public EtatEntite? Obtenir(EntiteSynchro entite, Guid id)
    {
        if (entite == EntiteSynchro.Reglage)
            return LireReglage();
        return Executer($"SELECT payload FROM {Tables[entite]} WHERE id = @id", cmd =>
        {
            cmd.Parameters.AddWithValue("@id", id.ToString());
            using var lecteur = cmd.ExecuteReader();
            return lecteur.Read() && !lecteur.IsDBNull(0) ? EtatDepuisPayload(entite, lecteur.GetString(0)) : null;
        });
    }

    /// <summary>Tous les états d'une entité (UI + export §5.7) — corbeille comprise (supprime = true).</summary>
    public IReadOnlyList<EtatEntite> Enumerer(EntiteSynchro entite)
    {
        if (entite == EntiteSynchro.Reglage)
        {
            var r = LireReglage();
            return r is null ? [] : [r];
        }
        return Executer($"SELECT payload FROM {Tables[entite]}", cmd =>
        {
            using var lecteur = cmd.ExecuteReader();
            var liste = new List<EtatEntite>();
            while (lecteur.Read())
                if (!lecteur.IsDBNull(0))
                    liste.Add(EtatDepuisPayload(entite, lecteur.GetString(0)));
            return liste;
        });
    }

    /// <summary>Destruction locale réelle (purge depuis la corbeille, §5.6) — jamais un soft delete.</summary>
    public void SupprimerReel(EntiteSynchro entite, Guid id)
    {
        if (entite == EntiteSynchro.Reglage)
            return; // le réglage n'est pas purgeable (§5.6)
        ExecuterNonQuery($"DELETE FROM {Tables[entite]} WHERE id = @id",
            cmd => cmd.Parameters.AddWithValue("@id", id.ToString()));
    }

    private void EcrireReglage(EtatEntite etat)
    {
        // On lit les champs de synchro depuis l'EtatEntite déjà résolu (le JSON canonique omet les nuls,
        // p. ex. server_seq d'un réglage pas encore poussé) — pas de nouvelle analyse du payload.
        void Lier(SqliteCommand cmd)
        {
            cmd.Parameters.AddWithValue("@cle", ReglageSolde.Cle);
            cmd.Parameters.AddWithValue("@valeur", etat.PayloadCanonique);
            cmd.Parameters.AddWithValue("@dm", etat.DateModification.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@v", etat.Version);
            cmd.Parameters.AddWithValue("@seq", etat.ServerSeq);
            cmd.Parameters.AddWithValue("@sup", etat.Supprime ? 1 : 0);
        }
        var maj = ExecuterNonQuery(
            "UPDATE settings SET valeur=@valeur, date_modification=@dm, version=@v, server_seq=@seq, supprime=@sup WHERE cle=@cle",
            Lier);
        if (maj == 0)
            ExecuterNonQuery(
                "INSERT INTO settings (cle, valeur, date_modification, version, server_seq, supprime) " +
                "VALUES (@cle, @valeur, @dm, @v, @seq, @sup)", Lier);
    }

    private EtatEntite? LireReglage()
        => Executer("SELECT valeur FROM settings WHERE cle = @cle", cmd =>
        {
            cmd.Parameters.AddWithValue("@cle", ReglageSolde.Cle);
            using var lecteur = cmd.ExecuteReader();
            return lecteur.Read() && !lecteur.IsDBNull(0)
                ? EtatDepuisPayload(EntiteSynchro.Reglage, lecteur.GetString(0)) : null;
        });

    // ----- Outbox persistante (§6.2) : survit au redémarrage -----

    public void AjouterOutbox(ChangementPush changement)
        => ExecuterNonQuery(
            "INSERT OR REPLACE INTO outbox (change_id, entite, element_id, version, payload, date_modification, appareil_id, cree_le) " +
            "VALUES (@cid, @ent, @eid, @ver, @pl, @dm, @app, @cree)",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@cid", changement.ChangeId.ToString());
                cmd.Parameters.AddWithValue("@ent", NomEntite(changement.Entite));
                cmd.Parameters.AddWithValue("@eid", changement.EntiteId.ToString());
                cmd.Parameters.AddWithValue("@ver", changement.Version);
                cmd.Parameters.AddWithValue("@pl", changement.Payload.GetRawText());
                cmd.Parameters.AddWithValue("@dm", changement.DateModification.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@app", changement.AppareilId.ToString());
                cmd.Parameters.AddWithValue("@cree", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            });

    /// <summary>Les changements en attente d'envoi, dans l'ordre de création (lots ordonnés, §6.2).</summary>
    public IReadOnlyList<ChangementPush> Outbox()
        => Executer(
            "SELECT change_id, entite, element_id, version, payload, date_modification, appareil_id FROM outbox ORDER BY cree_le, rowid",
            cmd =>
            {
                using var lecteur = cmd.ExecuteReader();
                var liste = new List<ChangementPush>();
                while (lecteur.Read())
                    liste.Add(new ChangementPush
                    {
                        ChangeId = Guid.Parse(lecteur.GetString(0)),
                        Entite = EntiteDepuis(lecteur.GetString(1)),
                        EntiteId = Guid.Parse(lecteur.GetString(2)),
                        Version = lecteur.GetInt32(3),
                        Payload = JsonDocument.Parse(lecteur.GetString(4)).RootElement.Clone(),
                        DateModification = DateTimeOffset.Parse(lecteur.GetString(5), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                        AppareilId = Guid.Parse(lecteur.GetString(6)),
                    });
                return liste;
            });

    /// <summary>Vide de l'outbox un changement confirmé par le serveur (§6.2, étape 5).</summary>
    public void RetirerOutbox(Guid changeId)
        => ExecuterNonQuery("DELETE FROM outbox WHERE change_id = @cid",
            cmd => cmd.Parameters.AddWithValue("@cid", changeId.ToString()));

    /// <summary>Abandonne les changements en attente d'une entité (purge acceptée / refus de résurrection, §5.6).</summary>
    public void ViderOutboxEntite(EntiteSynchro entite, Guid entiteId)
        => ExecuterNonQuery("DELETE FROM outbox WHERE entite = @ent AND element_id = @eid",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@ent", NomEntite(entite));
                cmd.Parameters.AddWithValue("@eid", entiteId.ToString());
            });

    // ----- État de synchro : curseur de pull, appareil_id (sync_etat) -----

    public string? LireEtat(string cle)
        => Executer("SELECT valeur FROM sync_etat WHERE cle = @cle", cmd =>
        {
            cmd.Parameters.AddWithValue("@cle", cle);
            using var lecteur = cmd.ExecuteReader();
            return lecteur.Read() ? lecteur.GetString(0) : null;
        });

    public void EcrireEtat(string cle, string valeur)
        => ExecuterNonQuery("INSERT OR REPLACE INTO sync_etat (cle, valeur) VALUES (@cle, @val)",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@cle", cle);
                cmd.Parameters.AddWithValue("@val", valeur);
            });

    // ----- Atomicité (application d'un pull, §6.2) -----

    public void DansTransaction(Action action)
    {
        using var tx = connexion.BeginTransaction();
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
    }

    // ----- Plomberie -----

    private void Peupler(SqliteCommand cmd, EtatEntite etat, string[] colonnes, JsonElement payload)
    {
        cmd.Parameters.AddWithValue("@id", etat.Id.ToString());
        foreach (var c in colonnes)
            cmd.Parameters.AddWithValue("@" + c, ValeurColonne(c, payload));
        cmd.Parameters.AddWithValue("@payload", etat.PayloadCanonique);
    }

    private static object ValeurColonne(string colonne, JsonElement payload)
    {
        var present = payload.TryGetProperty(colonne, out var p) && p.ValueKind != JsonValueKind.Null;
        // server_seq est NON NULL au schéma mais posé par le serveur (§6.2) : une entité créée en local
        // et pas encore poussée n'en a pas → 0. Le pull la remplacera par la valeur autoritaire du serveur.
        if (colonne == "server_seq")
            return present ? p.GetInt64() : 0L;
        if (!present)
            return DBNull.Value;
        return colonne switch
        {
            "version" => p.GetInt32(),
            "montant_periode_centimes" or "taille_octets" => p.GetInt64(),
            "journee_entiere" or "date_approximative" or "est_obligatoire" or "supprime" or "confirme"
                => p.GetBoolean() ? 1 : 0,
            _ => p.GetString()!, // texte, GUID et dates : stockés tels quels (chaînes canoniques)
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
            e.TryGetProperty("server_seq", out var seq) && seq.ValueKind != JsonValueKind.Null ? seq.GetInt64() : 0,
            payload);
    }

    private T Executer<T>(string sql, Func<SqliteCommand, T> op)
    {
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = sql;
        return op(cmd);
    }

    private int ExecuterNonQuery(string sql, Action<SqliteCommand> preparer)
        => Executer(sql, cmd => { preparer(cmd); return cmd.ExecuteNonQuery(); });

    // ----- Noms d'énumérations (miroir du JSON canonique et du serveur, D-007) -----

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
}
