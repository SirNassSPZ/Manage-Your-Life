using System.Globalization;
using DeuxiemeCerveau.Core.Migrations;
using Microsoft.Data.Sqlite;

namespace DeuxiemeCerveau.App.Local;

/// <summary>
/// Cible d'application des migrations (§9, règle 18) pour la base locale SQLite. Applique au démarrage
/// les MÊMES scripts que le serveur (dialecte local), depuis la liste unique du cœur — jamais de schéma
/// transcrit à la main : une divergence de schéma entre apps = perte de données silencieuse (D-008).
/// </summary>
public sealed class CibleMigrationLocale(SqliteConnection connexion) : ICibleMigration
{
    public IReadOnlyList<Migration> AppliquerAuDemarrage()
    {
        Executer("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
              numero INTEGER PRIMARY KEY, nom TEXT NOT NULL, applique_le TEXT NOT NULL);
            """);
        return ExecuteurMigrations.Appliquer(this, DialecteSql.Sqlite);
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
        cmd.Parameters.AddWithValue("@n", migration.Numero);
        cmd.Parameters.AddWithValue("@nom", migration.Nom);
        cmd.Parameters.AddWithValue("@le", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }
}
