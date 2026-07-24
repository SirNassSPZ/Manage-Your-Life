using System.Data.Common;
using DeuxiemeCerveau.Core.Migrations;
using Microsoft.Data.Sqlite;

namespace DeuxiemeCerveau.Windows.Core.Migrations;

/// <summary>
/// Cible de migration SQLite locale (§9, D-008). Applique les migrations 001..003 du cœur
/// en dialecte SQLite sur la base locale de l'appareil.
/// </summary>
public sealed class CibleMigrationSqlite : ICibleMigration
{
    private readonly string _chaineConnexion;

    public CibleMigrationSqlite(string chaineConnexion)
    {
        _chaineConnexion = chaineConnexion;
    }

    public int VersionCourante()
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();

        using var cmdTable = connexion.CreateCommand();
        cmdTable.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations'";
        var existe = Convert.ToInt32(cmdTable.ExecuteScalar());
        if (existe == 0)
            return 0;

        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(numero), 0) FROM schema_migrations";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Executer(string sql)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var tx = connexion.BeginTransaction();
        using var cmd = connexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public void MarquerAppliquee(Migration migration)
    {
        using var connexion = new SqliteConnection(_chaineConnexion);
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS schema_migrations (
                numero INT PRIMARY KEY,
                nom VARCHAR(100) NOT NULL,
                appliquee_le VARCHAR(30) NOT NULL
            );
            INSERT INTO schema_migrations (numero, nom, appliquee_le)
            VALUES (@numero, @nom, @date);";

        var pNum = cmd.CreateParameter();
        pNum.ParameterName = "@numero";
        pNum.Value = migration.Numero;
        cmd.Parameters.Add(pNum);

        var pNom = cmd.CreateParameter();
        pNom.ParameterName = "@nom";
        pNom.Value = migration.Nom;
        cmd.Parameters.Add(pNom);

        var pDate = cmd.CreateParameter();
        pDate.ParameterName = "@date";
        pDate.Value = DateTimeOffset.UtcNow.ToString("o");
        cmd.Parameters.Add(pDate);

        cmd.ExecuteNonQuery();
    }
}
