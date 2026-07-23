using System.Data.Common;
using System.Globalization;
using DeuxiemeCerveau.Core.Migrations;

namespace DeuxiemeCerveau.Api.Persistence;

/// <summary>
/// Cible de migration sur base relationnelle (adaptateur). Applique au démarrage les migrations
/// manquantes, à l'identique sur Azure SQL et SQLite (règle 18). La liste vit dans le cœur.
/// </summary>
public sealed class CibleMigrationSql(DbConnection connexion, DialecteSql dialecte) : ICibleMigration
{
    public void Preparer()
    {
        // Table de suivi des versions de schéma (§9). Créée si absente, dans les deux dialectes.
        var sql = dialecte == DialecteSql.Sqlite
            ? """
              CREATE TABLE IF NOT EXISTS schema_migrations (
                numero INTEGER PRIMARY KEY, nom TEXT NOT NULL, applique_le TEXT NOT NULL);
              """
            : """
              IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'schema_migrations')
                CREATE TABLE schema_migrations (
                  numero INT PRIMARY KEY, nom NVARCHAR(100) NOT NULL, applique_le DATETIME2 NOT NULL);
              """;
        Executer(sql);
    }

    public int VersionCourante()
    {
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(numero), 0) FROM schema_migrations";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Executer(string sql)
    {
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void MarquerAppliquee(Migration migration)
    {
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "INSERT INTO schema_migrations (numero, nom, applique_le) VALUES (@n, @nom, @le)";
        Param(cmd, "@n", migration.Numero);
        Param(cmd, "@nom", migration.Nom);
        Param(cmd, "@le", DateTime.UtcNow);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Applique au démarrage les migrations manquantes (règle 18).</summary>
    public IReadOnlyList<Migration> AppliquerAuDemarrage()
    {
        Preparer();
        return ExecuteurMigrations.Appliquer(this, dialecte);
    }

    private static void Param(DbCommand cmd, string nom, object valeur)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = nom;
        p.Value = valeur;
        cmd.Parameters.Add(p);
    }
}
