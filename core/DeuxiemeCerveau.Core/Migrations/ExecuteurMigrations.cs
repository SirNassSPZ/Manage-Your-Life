namespace DeuxiemeCerveau.Core.Migrations;

/// <summary>Dialecte SQL d'une base : Azure SQL (serveur) ou SQLite (bases locales des apps).</summary>
public enum DialecteSql
{
    AzureSql,
    Sqlite,
}

/// <summary>
/// Une migration numérotée : deux scripts équivalents, un par dialecte (D-008). La sémantique est
/// identique ; seuls les types diffèrent. Additive uniquement (règle 18).
/// </summary>
public sealed record Migration(int Numero, string Nom, string SqlAzure, string SqlLocal)
{
    public string Script(DialecteSql dialecte)
        => dialecte == DialecteSql.AzureSql ? SqlAzure : SqlLocal;
}

/// <summary>
/// Cible d'application des migrations — l'adaptateur fournit l'accès à la base (et la transaction
/// par migration s'il en dispose) ; le cœur fournit la liste et l'ordre.
/// </summary>
public interface ICibleMigration
{
    /// <summary>Numéro de la dernière migration appliquée, 0 pour une base vierge.</summary>
    int VersionCourante();

    void Executer(string sql);

    /// <summary>Enregistre la migration comme appliquée (table schema_migrations).</summary>
    void MarquerAppliquee(Migration migration);
}

/// <summary>
/// Applique les migrations manquantes dans l'ordre, au démarrage (règle 18 — identique sur
/// Azure SQL et chaque base locale). Refuse toute liste non contiguë ou non additive.
/// </summary>
public static class ExecuteurMigrations
{
    /// <summary>Vérifie l'intégrité de la liste : numéros contigus 1..n, sans doublon.</summary>
    public static void VerifierListe(IReadOnlyList<Migration> migrations)
    {
        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Numero != i + 1)
                throw new InvalidOperationException(
                    $"Liste de migrations invalide : position {i} porte le numéro {migrations[i].Numero} " +
                    $"(attendu {i + 1} — numérotation contiguë obligatoire, règle 18).");
            if (string.IsNullOrWhiteSpace(migrations[i].SqlAzure) || string.IsNullOrWhiteSpace(migrations[i].SqlLocal))
                throw new InvalidOperationException(
                    $"Migration {migrations[i].Numero} : les deux dialectes sont obligatoires (D-008).");
        }
    }

    /// <summary>Applique les migrations manquantes ; renvoie celles qui ont été appliquées.</summary>
    public static IReadOnlyList<Migration> Appliquer(ICibleMigration cible, DialecteSql dialecte)
        => Appliquer(cible, dialecte, ListeMigrations.Toutes);

    public static IReadOnlyList<Migration> Appliquer(
        ICibleMigration cible, DialecteSql dialecte, IReadOnlyList<Migration> migrations)
    {
        VerifierListe(migrations);

        var versionCourante = cible.VersionCourante();
        if (versionCourante > migrations.Count)
            throw new InvalidOperationException(
                $"La base est en version {versionCourante}, inconnue de cette liste ({migrations.Count} migrations) : " +
                "application plus récente requise — jamais de retour en arrière (règle 18).");

        var appliquees = new List<Migration>();
        foreach (var migration in migrations.Skip(versionCourante))
        {
            cible.Executer(migration.Script(dialecte));
            cible.MarquerAppliquee(migration);
            appliquees.Add(migration);
        }
        return appliquees;
    }
}
