using Microsoft.Data.SqlClient;

// Accorde à l'identité managée du Function App (nom = identité) un accès à la base Azure SQL,
// en tant qu'utilisateur « contained » Entra. Idempotent. Le jeton d'accès (SP admin de la base)
// est fourni par la variable d'environnement SQL_ACCESS_TOKEN — jamais dans le code (règle 16).
//
// Usage : dotnet run --project tools/AccorderAccesSql -- <fqdn> <base> <nom-identite>

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage : AccorderAccesSql <fqdn> <base> <nom-identite>");
    return 2;
}

var (fqdn, baseDonnees, identite) = (args[0], args[1], args[2]);
var jeton = Environment.GetEnvironmentVariable("SQL_ACCESS_TOKEN");
if (string.IsNullOrWhiteSpace(jeton))
{
    Console.Error.WriteLine("SQL_ACCESS_TOKEN manquant.");
    return 2;
}

var chaine = new SqlConnectionStringBuilder
{
    DataSource = $"tcp:{fqdn},1433",
    InitialCatalog = baseDonnees,
    Encrypt = true,
    ConnectTimeout = 120, // la base serverless peut être en pause (§10.1)
}.ConnectionString;

// QUOTENAME protège le nom du principal ; @identite reste paramétré.
const string sql = """
    DECLARE @n sysname = @identite;
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @n)
        EXEC('CREATE USER ' + QUOTENAME(@n) + ' FROM EXTERNAL PROVIDER;');
    EXEC('ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@n) + ';');
    """;

for (var tentative = 1; ; tentative++)
{
    try
    {
        using var connexion = new SqlConnection(chaine) { AccessToken = jeton };
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@identite", identite);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"✓ Accès SQL accordé à l'identité managée « {identite} ».");
        return 0;
    }
    catch (Exception ex) when (tentative < 5)
    {
        Console.WriteLine($"Reprise SQL (tentative {tentative}) : {ex.Message} — nouvel essai…");
        Thread.Sleep(TimeSpan.FromSeconds(5 * tentative));
    }
}
