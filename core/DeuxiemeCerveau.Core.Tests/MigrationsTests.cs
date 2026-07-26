using System.Text.RegularExpressions;
using DeuxiemeCerveau.Core.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DeuxiemeCerveau.Core.Tests;

/// <summary>Cible SQLite réelle — le comportement attendu des bases locales des deux apps (règle 18).</summary>
public sealed class CibleSqlite : ICibleMigration, IDisposable
{
    public SqliteConnection Connexion { get; }

    public CibleSqlite()
    {
        Connexion = new SqliteConnection("Data Source=:memory:");
        Connexion.Open();
        Executer("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
              numero      INTEGER PRIMARY KEY,
              nom         TEXT NOT NULL,
              applique_le TEXT NOT NULL
            );
            """);
    }

    public int VersionCourante()
    {
        using var commande = Connexion.CreateCommand();
        commande.CommandText = "SELECT COALESCE(MAX(numero), 0) FROM schema_migrations;";
        return Convert.ToInt32(commande.ExecuteScalar());
    }

    public void Executer(string sql)
    {
        using var commande = Connexion.CreateCommand();
        commande.CommandText = sql;
        commande.ExecuteNonQuery();
    }

    public void MarquerAppliquee(Migration migration)
    {
        using var commande = Connexion.CreateCommand();
        commande.CommandText = "INSERT INTO schema_migrations (numero, nom, applique_le) VALUES ($n, $nom, $le);";
        commande.Parameters.AddWithValue("$n", migration.Numero);
        commande.Parameters.AddWithValue("$nom", migration.Nom);
        commande.Parameters.AddWithValue("$le", DateTimeOffset.UtcNow.ToString("O"));
        commande.ExecuteNonQuery();
    }

    public List<string> Tables()
    {
        using var commande = Connexion.CreateCommand();
        commande.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var lecteur = commande.ExecuteReader();
        var tables = new List<string>();
        while (lecteur.Read())
            tables.Add(lecteur.GetString(0));
        return tables;
    }

    public List<string> Colonnes(string table)
    {
        using var commande = Connexion.CreateCommand();
        commande.CommandText = $"PRAGMA table_info({table});";
        using var lecteur = commande.ExecuteReader();
        var colonnes = new List<string>();
        while (lecteur.Read())
            colonnes.Add(lecteur.GetString(1));
        return colonnes;
    }

    /// <summary>Valeur d'une seule cellule — <see cref="DBNull.Value"/> quand la colonne est NULL.</summary>
    public object? Scalaire(string sql)
    {
        using var commande = Connexion.CreateCommand();
        commande.CommandText = sql;
        return commande.ExecuteScalar();
    }

    public void Dispose() => Connexion.Dispose();
}

public class MigrationsTests
{
    [Fact]
    public void Les_migrations_creent_tout_le_schema_local()
    {
        using var cible = new CibleSqlite();
        var appliquees = ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);

        Assert.Equal(ListeMigrations.Toutes.Count, appliquees.Count);
        Assert.Equal(ListeMigrations.Toutes.Count, cible.VersionCourante());

        // Toutes les tables du §9 (purges comprise, v3.2), plus outbox et sync_etat côté local (D-008).
        List<string> attendues =
        [
            "attachments", "budgets", "categories", "change_log", "devices",
            "element_categories", "elements", "outbox", "projets", "purges",
            "schema_migrations", "settings", "sync_etat",
        ];
        Assert.Equal(attendues, cible.Tables().Where(t => t != "sqlite_sequence").Order().ToList());
    }

    [Fact]
    public void Base_en_version_1_recoit_les_migrations_manquantes()
    {
        using var cible = new CibleSqlite();
        ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite, [ListeMigrations.Toutes[0]]);
        Assert.Equal(1, cible.VersionCourante());

        // Au démarrage suivant (liste complète), seules les migrations manquantes s'appliquent (règle 18).
        var appliquees = ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);
        Assert.Equal(Enumerable.Range(2, ListeMigrations.Toutes.Count - 1), appliquees.Select(m => m.Numero));
        Assert.Contains("purges", cible.Tables());
        Assert.Contains("payload", cible.Colonnes("elements"));
    }

    [Fact]
    public void Colonnes_des_elements_conformes_au_9()
    {
        using var cible = new CibleSqlite();
        ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);

        List<string> attendues =
        [
            "id", "type", "titre", "description", "date_debut", "date_fin", "fuseau",
            "journee_entiere", "date_approximative", "recurrence", "montant_centimes", "devise",
            "sens", "projet_id", "budget_id", "est_obligatoire", "score_points", "priorite",
            "ordre_manuel", "statut", "rappels", "date_creation", "date_modification",
            "appareil_source", "version", "server_seq", "supprime", "date_suppression",
            "payload", // ajoutée par la migration 003 (D-012)
        ];
        Assert.Equal(attendues, cible.Colonnes("elements"));
    }

    [Fact]
    public void Colonnes_des_categories_conformes_au_9()
    {
        using var cible = new CibleSqlite();
        ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);

        List<string> attendues =
        [
            "id", "nom", "couleur", "origine", "date_creation", "date_modification",
            "appareil_source", "version", "server_seq", "supprime", "date_suppression",
            "payload",       // migration 003 (D-012)
            "ordre", "icone", // migration 004 (§3.3 et §9, v3.3)
        ];
        Assert.Equal(attendues, cible.Colonnes("categories"));
    }

    /// <summary>
    /// Le cas réel de l'utilisateur : sa base tourne déjà en 003 <b>avec des données</b>. La 004
    /// doit s'y poser sans reprise et sans rien perdre — c'est tout l'intérêt d'une migration
    /// additive à colonnes NULL (règle 18).
    /// </summary>
    [Fact]
    public void Base_deja_en_003_avec_des_donnees_recoit_la_004_sans_rien_perdre()
    {
        using var cible = new CibleSqlite();
        ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite, [.. ListeMigrations.Toutes.Take(3)]);
        Assert.Equal(3, cible.VersionCourante());
        cible.Executer("""
            INSERT INTO categories (id, nom, couleur, origine, date_creation, date_modification,
                                    appareil_source, version, server_seq)
            VALUES ('c1', 'santé', '#00AA55', 'transversale', '2026-07-01T08:00:00Z',
                    '2026-07-01T08:00:00Z', 'a1', 1, 7);
            """);

        var appliquees = ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);

        Assert.Equal([4], appliquees.Select(m => m.Numero));
        Assert.Equal("santé", cible.Scalaire("SELECT nom FROM categories WHERE id = 'c1';"));
        Assert.Equal(7L, cible.Scalaire("SELECT server_seq FROM categories WHERE id = 'c1';"));
        // Les catégories déjà là n'ont ni rang ni pictogramme : facultatifs, donc NULL (§3.3).
        Assert.Equal(DBNull.Value, cible.Scalaire("SELECT ordre FROM categories WHERE id = 'c1';"));
        Assert.Equal(DBNull.Value, cible.Scalaire("SELECT icone FROM categories WHERE id = 'c1';"));
    }

    [Fact]
    public void Base_neuve_accepte_une_categorie_avec_ou_sans_ordre_ni_icone()
    {
        using var cible = new CibleSqlite();
        ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);
        cible.Executer("""
            INSERT INTO categories (id, nom, couleur, origine, ordre, icone, date_creation,
                                    date_modification, appareil_source, version, server_seq)
            VALUES ('c1', 'sport', '#00AA55', 'transversale', 2, '🏅', '2026-07-01T08:00:00Z',
                    '2026-07-01T08:00:00Z', 'a1', 1, 0);
            INSERT INTO categories (id, nom, couleur, origine, date_creation, date_modification,
                                    appareil_source, version, server_seq)
            VALUES ('c2', 'justice', '#00AA55', 'transversale', '2026-07-01T08:00:00Z',
                    '2026-07-01T08:00:00Z', 'a1', 1, 0);
            """);

        Assert.Equal(2L, cible.Scalaire("SELECT ordre FROM categories WHERE id = 'c1';"));
        Assert.Equal("🏅", cible.Scalaire("SELECT icone FROM categories WHERE id = 'c1';"));
        Assert.Equal(DBNull.Value, cible.Scalaire("SELECT ordre FROM categories WHERE id = 'c2';"));
        // Classement par nom quand le rang est absent (§3.3) — SQL trie les NULL en premier, donc
        // c'est bien au lecteur d'ordonner « ordre absent → par nom », pas à la base.
        Assert.Equal("justice", cible.Scalaire("SELECT nom FROM categories ORDER BY nom LIMIT 1;"));
    }

    [Fact]
    public void Reappliquer_est_sans_effet()
    {
        using var cible = new CibleSqlite();
        ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);
        var secondPassage = ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);
        Assert.Empty(secondPassage); // appliquées au démarrage, jamais deux fois (règle 18)
    }

    [Fact]
    public void Le_schema_local_accepte_les_donnees_de_base()
    {
        using var cible = new CibleSqlite();
        ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);
        cible.Executer("""
            INSERT INTO elements (id, type, titre, statut, date_creation, date_modification,
                                  appareil_source, version, server_seq)
            VALUES ('e1', 'facture', 'Loyer', 'a_venir', '2026-07-01T08:00:00Z',
                    '2026-07-01T08:00:00Z', 'a1', 1, 0);
            INSERT INTO outbox (change_id, entite, element_id, version, payload, date_modification,
                                appareil_id, cree_le)
            VALUES ('c1', 'element', 'e1', 1, '{}', '2026-07-01T08:00:00Z', 'a1', '2026-07-01T08:00:00Z');
            INSERT INTO change_log (change_id, entite, element_id, payload, appareil_id, resultat, recu_le)
            VALUES ('c1', 'element', 'e1', '{}', 'a1', 'applique', '2026-07-01T08:00:01Z');
            """);
    }

    [Fact]
    public void Liste_non_contigue_refusee()
    {
        var migration = ListeMigrations.Toutes[0];
        List<Migration> trouee = [migration, migration with { Numero = 3 }];
        Assert.Throws<InvalidOperationException>(() => ExecuteurMigrations.VerifierListe(trouee));
    }

    [Fact]
    public void Base_plus_recente_que_la_liste_refusee()
    {
        using var cible = new CibleSqlite();
        ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite);
        cible.MarquerAppliquee(ListeMigrations.Toutes[0] with { Numero = 9, Nom = "future" });
        Assert.Throws<InvalidOperationException>(() => ExecuteurMigrations.Appliquer(cible, DialecteSql.Sqlite));
    }

    [Fact]
    public void La_liste_officielle_est_valide()
        => ExecuteurMigrations.VerifierListe(ListeMigrations.Toutes);
}

/// <summary>
/// Parité structurelle entre les deux dialectes (D-008) : mêmes tables, mêmes colonnes, aux types
/// près — deux schémas divergents sont une cause directe de perte de données (règle 18).
/// Les créations de tables <b>et</b> les ajouts de colonnes sont comparés : une migration additive
/// (003, 004) ne passe pas par CREATE TABLE, et n'aurait donc rien fait vérifier.
/// </summary>
public class PariteDialectesTests
{
    private static Dictionary<string, List<string>> TablesEtColonnes(string script)
    {
        var resultat = new Dictionary<string, List<string>>();
        var sansCommentaires = Regex.Replace(script, "--[^\n]*", "");
        foreach (Match table in Regex.Matches(sansCommentaires,
            @"CREATE TABLE (\w+) \((.*?)\);", RegexOptions.Singleline))
        {
            var colonnes = new List<string>();
            foreach (var ligne in table.Groups[2].Value.Split(','))
            {
                var mot = Regex.Match(ligne.Trim(), @"^(\w+)");
                if (!mot.Success)
                    continue;
                var nom = mot.Groups[1].Value;
                if (nom is "PRIMARY" or "UNIQUE" or "CHECK" or "FOREIGN")
                    continue; // contrainte de table, pas une colonne
                colonnes.Add(nom);
            }
            resultat[table.Groups[1].Value] = colonnes;
        }
        // Les migrations additives ajoutent des colonnes à des tables déjà là : « ADD col »
        // (T-SQL) et « ADD COLUMN col » (SQLite) désignent la même colonne.
        foreach (Match ajout in Regex.Matches(sansCommentaires,
            @"ALTER TABLE (\w+)\s+ADD\s+(?:COLUMN\s+)?(\w+)"))
        {
            if (!resultat.TryGetValue(ajout.Groups[1].Value, out var colonnes))
                resultat[ajout.Groups[1].Value] = colonnes = [];
            colonnes.Add(ajout.Groups[2].Value);
        }
        return resultat;
    }

    /// <summary>
    /// Le lecteur de scripts voit réellement les deux formes d'ajout de colonne — sinon la parité
    /// des migrations additives serait « verte » en ne comparant rien du tout.
    /// </summary>
    [Fact]
    public void Le_lecteur_de_scripts_voit_les_colonnes_ajoutees()
    {
        Assert.Equal(["ordre", "icone"],
            TablesEtColonnes(Migration004CategorieOrdreIcone.Definition.SqlAzure)["categories"]);
        Assert.Equal(["ordre", "icone"],
            TablesEtColonnes(Migration004CategorieOrdreIcone.Definition.SqlLocal)["categories"]);
        Assert.Equal(["payload"],
            TablesEtColonnes(Migration003Payload.Definition.SqlAzure)["elements"]);
    }

    [Fact]
    public void Les_deux_dialectes_definissent_le_meme_schema()
    {
        foreach (var migration in ListeMigrations.Toutes)
        {
            var azure = TablesEtColonnes(migration.SqlAzure);
            var local = TablesEtColonnes(migration.SqlLocal);

            // Tables locales supplémentaires explicitement listées (D-008) — rien d'autre.
            HashSet<string> localesSeules = ["outbox", "sync_etat"];
            Assert.Equal(azure.Keys.Order(),
                local.Keys.Where(t => !localesSeules.Contains(t)).Order());

            foreach (var (tableAzure, colonnesAzure) in azure)
                Assert.Equal(colonnesAzure, local[tableAzure]);
        }
    }

    [Fact]
    public void Le_dialecte_azure_reprend_le_ddl_du_9()
    {
        var azure = TablesEtColonnes(ListeMigrations.Toutes[0].SqlAzure);
        // Les 9 tables du §9 (settings comprise) — aucune ne manque.
        List<string> attendues =
        [
            "attachments", "budgets", "categories", "change_log", "devices",
            "element_categories", "elements", "projets", "settings",
        ];
        Assert.Equal(attendues, azure.Keys.Order().ToList());
        Assert.Contains("entite", azure["change_log"]);   // D-006
        Assert.Contains("change_id", azure["change_log"]);
        Assert.Contains("server_seq", azure["elements"]);
        Assert.Contains("supprime", azure["elements"]);   // filet 2 : marquage, jamais destruction
        Assert.Contains("budget_id", azure["elements"]);  // §3.6 dès la V1 (stabilité du schéma)
    }
}
