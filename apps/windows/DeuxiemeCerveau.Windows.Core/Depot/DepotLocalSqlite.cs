using System.Data;
using System.Globalization;
using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Migrations;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Windows.Core.Migrations;
using DeuxiemeCerveau.Windows.Core.Synchro;
using Microsoft.Data.Sqlite;

namespace DeuxiemeCerveau.Windows.Core.Depot;

public sealed class DepotLocalSqlite : IDepotLocal
{
    private readonly string _chaineConnexion;
    private Guid _appareilId;

    public Guid AppareilId => _appareilId;

    public DepotLocalSqlite(string chaineConnexion, Guid? appareilId = null)
    {
        _chaineConnexion = chaineConnexion;
        _appareilId = appareilId ?? Guid.NewGuid();

        // Initialiser la base avec les migrations et les tables locales
        var cible = new CibleMigrationSqlite(_chaineConnexion);
        ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);
        InitialiserTablesLocales();

        if (appareilId.HasValue)
        {
            SauvegarderAppareilId(_appareilId);
        }
        else
        {
            var stocke = ObtenirAppareilIdStocke();
            if (stocke.HasValue)
                _appareilId = stocke.Value;
            else
                SauvegarderAppareilId(_appareilId);
        }
    }

    private void InitialiserTablesLocales()
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS outbox (
                change_id TEXT PRIMARY KEY,
                entite TEXT NOT NULL,
                element_id TEXT NOT NULL,
                version INT NOT NULL,
                payload TEXT NOT NULL,
                date_modification TEXT NOT NULL,
                appareil_id TEXT NOT NULL,
                cree_le TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_etat (
                cle TEXT PRIMARY KEY,
                valeur TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();
    }

    public void DefinirAppareilId(Guid id)
    {
        _appareilId = id;
        SauvegarderAppareilId(id);
    }

    private Guid? ObtenirAppareilIdStocke()
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT valeur FROM sync_etat WHERE cle = 'appareil_id'";
        var v = cmd.ExecuteScalar() as string;
        return Guid.TryParse(v, out var id) ? id : null;
    }

    private void SauvegarderAppareilId(Guid id)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO sync_etat (cle, valeur) VALUES ('appareil_id', @valeur)";
        cmd.Parameters.AddWithValue("@valeur", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public long ObtenirCurseurPull()
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT valeur FROM sync_etat WHERE cle = 'curseur_pull'";
        var v = cmd.ExecuteScalar() as string;
        return long.TryParse(v, CultureInfo.InvariantCulture, out var c) ? c : 0;
    }

    public void SauvegarderCurseurPull(long curseur)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO sync_etat (cle, valeur) VALUES ('curseur_pull', @valeur)";
        cmd.Parameters.AddWithValue("@valeur", curseur.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    // ----- ÉLÉMENTS -----

    public Element? ObtenirElement(Guid id)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT * FROM elements WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var lecteur = cmd.ExecuteReader();
        return lecteur.Read() ? LireElement(lecteur) : null;
    }

    public IReadOnlyList<Element> ListerElements(bool inclureSupprimes = false)
    {
        var liste = new List<Element>();
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = inclureSupprimes
            ? "SELECT * FROM elements"
            : "SELECT * FROM elements WHERE supprime = 0";
        using var lecteur = cmd.ExecuteReader();
        while (lecteur.Read())
        {
            liste.Add(LireElement(lecteur));
        }
        return liste;
    }

    public void EnregistrerElement(Element element)
    {
        var maintenant = DateTimeOffset.UtcNow;
        if (element.Id == Guid.Empty)
            element.Id = Guid.NewGuid();

        if (element.DateCreation == default)
            element.DateCreation = maintenant;

        element.DateModification = maintenant;
        element.AppareilSource = _appareilId;
        element.Version++;

        var payload = SerialisationCanonique.Serialiser(element);

        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var tx = connexion.BeginTransaction();

        // 1. Écriture locale (elements)
        SauvegarderElementSql(connexion, tx, element);

        // 2. Écriture Outbox
        AjouterOutboxSql(connexion, tx, EntiteSynchro.Element, element.Id, element.Version, payload, element.DateModification);

        tx.Commit();
    }

    public void SupprimerElement(Guid id)
    {
        var elem = ObtenirElement(id);
        if (elem is null || elem.Supprime) return;

        elem.Supprime = true;
        elem.DateSuppression = DateTimeOffset.UtcNow;
        EnregistrerElement(elem);
    }

    public void RestaurerElement(Guid id)
    {
        var elem = ObtenirElement(id);
        if (elem is null || !elem.Supprime) return;

        elem.Supprime = false;
        elem.DateSuppression = null;
        EnregistrerElement(elem);
    }

    // ----- CATÉGORIES -----

    public Categorie? ObtenirCategorie(Guid id)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT * FROM categories WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var lecteur = cmd.ExecuteReader();
        return lecteur.Read() ? LireCategorie(lecteur) : null;
    }

    public IReadOnlyList<Categorie> ListerCategories(bool inclureSupprimes = false)
    {
        var liste = new List<Categorie>();
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = inclureSupprimes
            ? "SELECT * FROM categories"
            : "SELECT * FROM categories WHERE supprime = 0";
        using var lecteur = cmd.ExecuteReader();
        while (lecteur.Read())
        {
            liste.Add(LireCategorie(lecteur));
        }
        return liste;
    }

    public void EnregistrerCategorie(Categorie categorie)
    {
        var maintenant = DateTimeOffset.UtcNow;
        if (categorie.Id == Guid.Empty)
            categorie.Id = Guid.NewGuid();

        if (categorie.DateCreation == default)
            categorie.DateCreation = maintenant;

        categorie.DateModification = maintenant;
        categorie.AppareilSource = _appareilId;
        categorie.Version++;

        var payload = SerialisationCanonique.Serialiser(categorie);

        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var tx = connexion.BeginTransaction();

        SauvegarderCategorieSql(connexion, tx, categorie);
        AjouterOutboxSql(connexion, tx, EntiteSynchro.Categorie, categorie.Id, categorie.Version, payload, categorie.DateModification);

        tx.Commit();
    }

    // ----- PROJETS -----

    public Projet? ObtenirProjet(Guid id)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT * FROM projets WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var lecteur = cmd.ExecuteReader();
        return lecteur.Read() ? LireProjet(lecteur) : null;
    }

    public IReadOnlyList<Projet> ListerProjets(bool inclureSupprimes = false)
    {
        var liste = new List<Projet>();
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = inclureSupprimes
            ? "SELECT * FROM projets"
            : "SELECT * FROM projets WHERE supprime = 0";
        using var lecteur = cmd.ExecuteReader();
        while (lecteur.Read())
        {
            liste.Add(LireProjet(lecteur));
        }
        return liste;
    }

    public void EnregistrerProjet(Projet projet)
    {
        var maintenant = DateTimeOffset.UtcNow;
        if (projet.Id == Guid.Empty)
            projet.Id = Guid.NewGuid();

        if (projet.DateCreation == default)
            projet.DateCreation = maintenant;

        projet.DateModification = maintenant;
        projet.AppareilSource = _appareilId;
        projet.Version++;

        var payload = SerialisationCanonique.Serialiser(projet);

        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var tx = connexion.BeginTransaction();

        SauvegarderProjetSql(connexion, tx, projet);
        AjouterOutboxSql(connexion, tx, EntiteSynchro.Projet, projet.Id, projet.Version, payload, projet.DateModification);

        tx.Commit();
    }

    // ----- BUDGETS -----

    public Budget? ObtenirBudget(Guid id)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT * FROM budgets WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var lecteur = cmd.ExecuteReader();
        return lecteur.Read() ? LireBudget(lecteur) : null;
    }

    public IReadOnlyList<Budget> ListerBudgets(bool inclureSupprimes = false)
    {
        var liste = new List<Budget>();
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = inclureSupprimes
            ? "SELECT * FROM budgets"
            : "SELECT * FROM budgets WHERE supprime = 0";
        using var lecteur = cmd.ExecuteReader();
        while (lecteur.Read())
        {
            liste.Add(LireBudget(lecteur));
        }
        return liste;
    }

    public void EnregistrerBudget(Budget budget)
    {
        var maintenant = DateTimeOffset.UtcNow;
        if (budget.Id == Guid.Empty)
            budget.Id = Guid.NewGuid();

        if (budget.DateCreation == default)
            budget.DateCreation = maintenant;

        budget.DateModification = maintenant;
        budget.AppareilSource = _appareilId;
        budget.Version++;

        var payload = SerialisationCanonique.Serialiser(budget);

        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var tx = connexion.BeginTransaction();

        SauvegarderBudgetSql(connexion, tx, budget);
        AjouterOutboxSql(connexion, tx, EntiteSynchro.Budget, budget.Id, budget.Version, payload, budget.DateModification);

        tx.Commit();
    }

    // ----- SOLDE DE RÉFÉRENCE -----

    public ReglageSolde? ObtenirSoldeReference()
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT valeur FROM settings WHERE cle = 'solde_reference'";
        var json = cmd.ExecuteScalar() as string;
        return json is not null ? SerialisationCanonique.Deserialiser<ReglageSolde>(json) : null;
    }

    public void RecalerSoldeReference(ReglageSolde reglage, Guid changeId)
    {
        var maintenant = DateTimeOffset.UtcNow;
        reglage.DateModification = maintenant;
        reglage.AppareilSource = _appareilId;
        reglage.Version++;

        var json = SerialisationCanonique.Serialiser(reglage);

        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var tx = connexion.BeginTransaction();

        using (var cmd = connexion.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT OR REPLACE INTO settings (cle, valeur, date_modification, date_creation, appareil_source, version, server_seq, supprime)
                VALUES ('solde_reference', @valeur, @dateMod, @dateMod, @appareil, @version, 0, 0);";
            cmd.Parameters.AddWithValue("@valeur", json);
            cmd.Parameters.AddWithValue("@dateMod", maintenant.ToString("o"));
            cmd.Parameters.AddWithValue("@appareil", _appareilId.ToString());
            cmd.Parameters.AddWithValue("@version", reglage.Version);
            cmd.ExecuteNonQuery();
        }

        AjouterOutboxSql(connexion, tx, EntiteSynchro.Reglage, ReglageSolde.IdSoldeReference, reglage.Version, json, maintenant, changeId);

        tx.Commit();
    }

    // ----- OUTBOX & PULL -----

    public IReadOnlyList<EntreeOutbox> ObtenirOutbox()
    {
        var liste = new List<EntreeOutbox>();
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT * FROM outbox ORDER BY cree_le ASC";
        using var lecteur = cmd.ExecuteReader();
        while (lecteur.Read())
        {
            liste.Add(new EntreeOutbox
            {
                ChangeId = Guid.Parse(lecteur.GetString(0)),
                Entite = Enum.Parse<EntiteSynchro>(lecteur.GetString(1)),
                EntiteId = Guid.Parse(lecteur.GetString(2)),
                Version = lecteur.GetInt32(3),
                Payload = lecteur.GetString(4),
                DateModification = DateTimeOffset.Parse(lecteur.GetString(5), CultureInfo.InvariantCulture),
                AppareilId = Guid.Parse(lecteur.GetString(6)),
                CreeLe = DateTimeOffset.Parse(lecteur.GetString(7), CultureInfo.InvariantCulture)
            });
        }
        return liste;
    }

    public void NettoyerOutbox(IEnumerable<Guid> changeIdsConfirmes)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var tx = connexion.BeginTransaction();
        foreach (var changeId in changeIdsConfirmes)
        {
            using var cmd = connexion.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM outbox WHERE change_id = @id";
            cmd.Parameters.AddWithValue("@id", changeId.ToString());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void AppliquerPull(PagePull page)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var tx = connexion.BeginTransaction();

        foreach (var etat in page.Entites)
        {
            switch (etat.Entite)
            {
                case EntiteSynchro.Element:
                    var elem = SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);
                    elem.ServerSeq = etat.ServerSeq;
                    SauvegarderElementSql(connexion, tx, elem);
                    break;
                case EntiteSynchro.Categorie:
                    var cat = SerialisationCanonique.Deserialiser<Categorie>(etat.PayloadCanonique);
                    cat.ServerSeq = etat.ServerSeq;
                    SauvegarderCategorieSql(connexion, tx, cat);
                    break;
                case EntiteSynchro.Projet:
                    var prj = SerialisationCanonique.Deserialiser<Projet>(etat.PayloadCanonique);
                    prj.ServerSeq = etat.ServerSeq;
                    SauvegarderProjetSql(connexion, tx, prj);
                    break;
                case EntiteSynchro.Budget:
                    var bdg = SerialisationCanonique.Deserialiser<Budget>(etat.PayloadCanonique);
                    bdg.ServerSeq = etat.ServerSeq;
                    SauvegarderBudgetSql(connexion, tx, bdg);
                    break;
                case EntiteSynchro.Reglage:
                    using (var cmd = connexion.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT OR REPLACE INTO settings (cle, valeur, date_modification, server_seq, supprime)
                            VALUES ('solde_reference', @valeur, @dateMod, @seq, @supprime);";
                        cmd.Parameters.AddWithValue("@valeur", etat.PayloadCanonique);
                        cmd.Parameters.AddWithValue("@dateMod", etat.DateModification.ToString("o"));
                        cmd.Parameters.AddWithValue("@seq", etat.ServerSeq);
                        cmd.Parameters.AddWithValue("@supprime", etat.Supprime ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                    break;
            }
        }

        foreach (var tombale in page.Purges)
        {
            PurgeLocaleInterne(connexion, tx, tombale.Entite, tombale.Id);
        }

        SauvegarderCurseurSql(connexion, tx, page.Curseur);

        tx.Commit();
    }

    public void PurgerLocalement(EntiteSynchro entite, Guid id)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var tx = connexion.BeginTransaction();
        PurgeLocaleInterne(connexion, tx, entite, id);
        tx.Commit();
    }

    private static void PurgeLocaleInterne(SqliteConnection connexion, SqliteTransaction tx, EntiteSynchro entite, Guid id)
    {
        var table = entite switch
        {
            EntiteSynchro.Element => "elements",
            EntiteSynchro.Categorie => "categories",
            EntiteSynchro.Projet => "projets",
            EntiteSynchro.Budget => "budgets",
            _ => null
        };

        if (table is not null)
        {
            using var cmd = connexion.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    // ----- SQL HELPERS -----

    private static void SauvegarderElementSql(SqliteConnection connexion, SqliteTransaction tx, Element elem)
    {
        using var cmd = connexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO elements (
                id, type, titre, description, date_debut, date_fin, fuseau, journee_entiere, date_approximative,
                recurrence, montant_centimes, devise, sens, projet_id, budget_id, est_obligatoire, score_points,
                priorite, ordre_manuel, statut, rappels, date_creation, date_modification, appareil_source, version,
                server_seq, supprime, date_suppression
            ) VALUES (
                @id, @type, @titre, @desc, @debut, @fin, @fuseau, @jEntiere, @dApprox, @recurrence, @montant,
                @devise, @sens, @projetId, @budgetId, @estOblig, @score, @priorite, @ordre, @statut, @rappels, @dCreation,
                @dMod, @appareil, @version, @serverSeq, @supprime, @dSuppr
            );";

        cmd.Parameters.AddWithValue("@id", elem.Id.ToString());
        cmd.Parameters.AddWithValue("@type", elem.Type.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@titre", elem.Titre);
        cmd.Parameters.AddWithValue("@desc", (object?)elem.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@debut", (object?)elem.DateDebut?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fin", (object?)elem.DateFin?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fuseau", (object?)elem.Fuseau ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@jEntiere", elem.JourneeEntiere ? 1 : 0);
        cmd.Parameters.AddWithValue("@dApprox", elem.DateApproximative ? 1 : 0);
        cmd.Parameters.AddWithValue("@recurrence", (object?)elem.Recurrence ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@montant", (object?)elem.MontantCentimes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@devise", (object?)elem.Devise ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sens", (object?)elem.Sens?.ToString().ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@projetId", (object?)elem.ProjetId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@budgetId", (object?)elem.BudgetId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@estOblig", elem.EstObligatoire ? 1 : 0);
        cmd.Parameters.AddWithValue("@score", (object?)elem.ScorePoints ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@priorite", (object?)elem.Priorite?.ToString().ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ordre", (object?)elem.OrdreManuel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@statut", elem.Statut.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@rappels", elem.Rappels.Count > 0 ? SerialisationCanonique.Serialiser(elem.Rappels) : DBNull.Value);
        cmd.Parameters.AddWithValue("@dCreation", elem.DateCreation.ToString("o"));
        cmd.Parameters.AddWithValue("@dMod", elem.DateModification.ToString("o"));
        cmd.Parameters.AddWithValue("@appareil", elem.AppareilSource.ToString());
        cmd.Parameters.AddWithValue("@version", elem.Version);
        cmd.Parameters.AddWithValue("@serverSeq", elem.ServerSeq ?? 0);
        cmd.Parameters.AddWithValue("@supprime", elem.Supprime ? 1 : 0);
        cmd.Parameters.AddWithValue("@dSuppr", (object?)elem.DateSuppression?.ToString("o") ?? DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    private static void SauvegarderCategorieSql(SqliteConnection connexion, SqliteTransaction tx, Categorie cat)
    {
        using var cmd = connexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO categories (id, nom, couleur, origine, date_creation, date_modification, appareil_source, version, server_seq, supprime, date_suppression)
            VALUES (@id, @nom, @couleur, @origine, @dCreation, @dMod, @appareil, @version, @seq, @suppr, @dSuppr);";
        cmd.Parameters.AddWithValue("@id", cat.Id.ToString());
        cmd.Parameters.AddWithValue("@nom", cat.Nom);
        cmd.Parameters.AddWithValue("@couleur", cat.Couleur);
        cmd.Parameters.AddWithValue("@origine", cat.Origine.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@dCreation", cat.DateCreation.ToString("o"));
        cmd.Parameters.AddWithValue("@dMod", cat.DateModification.ToString("o"));
        cmd.Parameters.AddWithValue("@appareil", cat.AppareilSource.ToString());
        cmd.Parameters.AddWithValue("@version", cat.Version);
        cmd.Parameters.AddWithValue("@seq", cat.ServerSeq ?? 0);
        cmd.Parameters.AddWithValue("@suppr", cat.Supprime ? 1 : 0);
        cmd.Parameters.AddWithValue("@dSuppr", (object?)cat.DateSuppression?.ToString("o") ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void SauvegarderProjetSql(SqliteConnection connexion, SqliteTransaction tx, Projet prj)
    {
        using var cmd = connexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO projets (id, nom, couleur, categorie_id, statut, date_creation, date_modification, appareil_source, version, server_seq, supprime, date_suppression)
            VALUES (@id, @nom, @couleur, @catId, @statut, @dCreation, @dMod, @appareil, @version, @seq, @suppr, @dSuppr);";
        cmd.Parameters.AddWithValue("@id", prj.Id.ToString());
        cmd.Parameters.AddWithValue("@nom", prj.Nom);
        cmd.Parameters.AddWithValue("@couleur", prj.Couleur);
        cmd.Parameters.AddWithValue("@catId", (object?)prj.CategorieId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@statut", prj.Statut.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@dCreation", prj.DateCreation.ToString("o"));
        cmd.Parameters.AddWithValue("@dMod", prj.DateModification.ToString("o"));
        cmd.Parameters.AddWithValue("@appareil", prj.AppareilSource.ToString());
        cmd.Parameters.AddWithValue("@version", prj.Version);
        cmd.Parameters.AddWithValue("@seq", prj.ServerSeq ?? 0);
        cmd.Parameters.AddWithValue("@suppr", prj.Supprime ? 1 : 0);
        cmd.Parameters.AddWithValue("@dSuppr", (object?)prj.DateSuppression?.ToString("o") ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void SauvegarderBudgetSql(SqliteConnection connexion, SqliteTransaction tx, Budget bdg)
    {
        using var cmd = connexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO budgets (id, nom, couleur, montant_periode_centimes, periode, statut, date_creation, date_modification, appareil_source, version, server_seq, supprime, date_suppression)
            VALUES (@id, @nom, @couleur, @montant, @periode, @statut, @dCreation, @dMod, @appareil, @version, @seq, @suppr, @dSuppr);";
        cmd.Parameters.AddWithValue("@id", bdg.Id.ToString());
        cmd.Parameters.AddWithValue("@nom", bdg.Nom);
        cmd.Parameters.AddWithValue("@couleur", bdg.Couleur);
        cmd.Parameters.AddWithValue("@montant", bdg.MontantPeriodeCentimes);
        cmd.Parameters.AddWithValue("@periode", bdg.Periode.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@statut", bdg.Statut.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@dCreation", bdg.DateCreation.ToString("o"));
        cmd.Parameters.AddWithValue("@dMod", bdg.DateModification.ToString("o"));
        cmd.Parameters.AddWithValue("@appareil", bdg.AppareilSource.ToString());
        cmd.Parameters.AddWithValue("@version", bdg.Version);
        cmd.Parameters.AddWithValue("@seq", bdg.ServerSeq ?? 0);
        cmd.Parameters.AddWithValue("@suppr", bdg.Supprime ? 1 : 0);
        cmd.Parameters.AddWithValue("@dSuppr", (object?)bdg.DateSuppression?.ToString("o") ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private void AjouterOutboxSql(SqliteConnection connexion, SqliteTransaction tx, EntiteSynchro entite, Guid entiteId, int version, string payload, DateTimeOffset dateMod, Guid? changeId = null)
    {
        using var cmd = connexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO outbox (change_id, entite, element_id, version, payload, date_modification, appareil_id, cree_le)
            VALUES (@changeId, @entite, @entiteId, @version, @payload, @dateMod, @appareil, @creeLe);";
        cmd.Parameters.AddWithValue("@changeId", (changeId ?? Guid.NewGuid()).ToString());
        cmd.Parameters.AddWithValue("@entite", entite.ToString());
        cmd.Parameters.AddWithValue("@entiteId", entiteId.ToString());
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@payload", payload);
        cmd.Parameters.AddWithValue("@dateMod", dateMod.ToString("o"));
        cmd.Parameters.AddWithValue("@appareil", _appareilId.ToString());
        cmd.Parameters.AddWithValue("@creeLe", DateTimeOffset.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    private static void SauvegarderCurseurSql(SqliteConnection connexion, SqliteTransaction tx, long curseur)
    {
        using var cmd = connexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO sync_etat (cle, valeur) VALUES ('curseur_pull', @valeur)";
        cmd.Parameters.AddWithValue("@valeur", curseur.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private static Element LireElement(SqliteDataReader l)
    {
        var jsonRappels = l.IsDBNull(20) ? null : l.GetString(20);
        var rappels = jsonRappels is not null
            ? SerialisationCanonique.Deserialiser<List<Rappel>>(jsonRappels)
            : new List<Rappel>();

        return new Element
        {
            Id = Guid.Parse(l.GetString(0)),
            Type = Enum.Parse<TypeElement>(l.GetString(1), true),
            Titre = l.GetString(2),
            Description = l.IsDBNull(3) ? null : l.GetString(3),
            DateDebut = l.IsDBNull(4) ? null : DateTimeOffset.Parse(l.GetString(4), CultureInfo.InvariantCulture),
            DateFin = l.IsDBNull(5) ? null : DateTimeOffset.Parse(l.GetString(5), CultureInfo.InvariantCulture),
            Fuseau = l.IsDBNull(6) ? null : l.GetString(6),
            JourneeEntiere = l.GetInt32(7) == 1,
            DateApproximative = l.GetInt32(8) == 1,
            Recurrence = l.IsDBNull(9) ? null : l.GetString(9),
            MontantCentimes = l.IsDBNull(10) ? null : l.GetInt64(10),
            Devise = l.IsDBNull(11) ? null : l.GetString(11),
            Sens = l.IsDBNull(12) ? null : Enum.Parse<Sens>(l.GetString(12), true),
            ProjetId = l.IsDBNull(13) ? null : Guid.Parse(l.GetString(13)),
            BudgetId = l.IsDBNull(14) ? null : Guid.Parse(l.GetString(14)),
            EstObligatoire = l.GetInt32(15) == 1,
            ScorePoints = l.IsDBNull(16) ? null : l.GetInt32(16),
            Priorite = l.IsDBNull(17) ? null : Enum.Parse<Priorite>(l.GetString(17), true),
            OrdreManuel = l.IsDBNull(18) ? null : l.GetInt32(18),
            Statut = Enum.Parse<StatutElement>(l.GetString(19), true),
            Rappels = rappels,
            DateCreation = DateTimeOffset.Parse(l.GetString(21), CultureInfo.InvariantCulture),
            DateModification = DateTimeOffset.Parse(l.GetString(22), CultureInfo.InvariantCulture),
            AppareilSource = Guid.Parse(l.GetString(23)),
            Version = l.GetInt32(24),
            ServerSeq = l.GetInt64(25) == 0 ? null : l.GetInt64(25),
            Supprime = l.GetInt32(26) == 1,
            DateSuppression = l.IsDBNull(27) ? null : DateTimeOffset.Parse(l.GetString(27), CultureInfo.InvariantCulture)
        };
    }

    private static Categorie LireCategorie(SqliteDataReader l)
    {
        return new Categorie
        {
            Id = Guid.Parse(l.GetString(0)),
            Nom = l.GetString(1),
            Couleur = l.GetString(2),
            Origine = Enum.Parse<OrigineCategorie>(l.GetString(3), true),
            DateCreation = DateTimeOffset.Parse(l.GetString(4), CultureInfo.InvariantCulture),
            DateModification = DateTimeOffset.Parse(l.GetString(5), CultureInfo.InvariantCulture),
            AppareilSource = Guid.Parse(l.GetString(6)),
            Version = l.GetInt32(7),
            ServerSeq = l.GetInt64(8) == 0 ? null : l.GetInt64(8),
            Supprime = l.GetInt32(9) == 1,
            DateSuppression = l.IsDBNull(10) ? null : DateTimeOffset.Parse(l.GetString(10), CultureInfo.InvariantCulture)
        };
    }

    private static Projet LireProjet(SqliteDataReader l)
    {
        return new Projet
        {
            Id = Guid.Parse(l.GetString(0)),
            Nom = l.GetString(1),
            Couleur = l.GetString(2),
            CategorieId = l.IsDBNull(3) ? null : Guid.Parse(l.GetString(3)),
            Statut = Enum.Parse<StatutProjet>(l.GetString(4), true),
            DateCreation = DateTimeOffset.Parse(l.GetString(5), CultureInfo.InvariantCulture),
            DateModification = DateTimeOffset.Parse(l.GetString(6), CultureInfo.InvariantCulture),
            AppareilSource = Guid.Parse(l.GetString(7)),
            Version = l.GetInt32(8),
            ServerSeq = l.GetInt64(9) == 0 ? null : l.GetInt64(9),
            Supprime = l.GetInt32(10) == 1,
            DateSuppression = l.IsDBNull(11) ? null : DateTimeOffset.Parse(l.GetString(11), CultureInfo.InvariantCulture)
        };
    }

    private static Budget LireBudget(SqliteDataReader l)
    {
        return new Budget
        {
            Id = Guid.Parse(l.GetString(0)),
            Nom = l.GetString(1),
            Couleur = l.GetString(2),
            MontantPeriodeCentimes = l.GetInt64(3),
            Periode = Enum.Parse<PeriodeBudget>(l.GetString(4), true),
            Statut = Enum.Parse<StatutBudget>(l.GetString(5), true),
            DateCreation = DateTimeOffset.Parse(l.GetString(6), CultureInfo.InvariantCulture),
            DateModification = DateTimeOffset.Parse(l.GetString(7), CultureInfo.InvariantCulture),
            AppareilSource = Guid.Parse(l.GetString(8)),
            Version = l.GetInt32(9),
            ServerSeq = l.GetInt64(10) == 0 ? null : l.GetInt64(10),
            Supprime = l.GetInt32(11) == 1,
            DateSuppression = l.IsDBNull(12) ? null : DateTimeOffset.Parse(l.GetString(12), CultureInfo.InvariantCulture)
        };
    }

    public void Dispose()
    {
        // Nettoyage des ressources SQL si nécessaire
    }
}
