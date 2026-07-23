using Microsoft.Data.SqlClient;

// Accorde à l'identité managée du Function App un accès à la base Azure SQL, en tant qu'utilisateur
// « contained » Entra, DÉCLARÉ PAR SON SID (identifiant client), sans interrogation de l'annuaire —
// le serveur SQL n'ayant pas de permission Directory Reader (voir aka.ms/sqlaadsetup). Idempotent.
// Le jeton d'accès (SP admin de la base) vient de SQL_ACCESS_TOKEN (règle 16).
//
// Usage : dotnet run --project tools/AccorderAccesSql -- <fqdn> <base> <nom-identite> <client-id>

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage : AccorderAccesSql <fqdn> <base> <nom-identite> <client-id>");
    return 2;
}

var (fqdn, baseDonnees, identite, clientId) = (args[0], args[1], args[2], args[3]);
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

// Le SID de l'utilisateur Entra = le client_id de l'identité, converti en binaire (ordre GUID standard).
// On construit la commande dans une variable puis EXEC (T-SQL n'accepte pas d'appel de fonction inline).
const string sql = """
    DECLARE @n sysname = @identite;
    DECLARE @sid varbinary(16) = CAST(CAST(@clientId AS uniqueidentifier) AS varbinary(16));
    DECLARE @sidHex nvarchar(100) = CONVERT(nvarchar(100), @sid, 1);
    DECLARE @sql nvarchar(max);
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @n)
    BEGIN
        SET @sql = N'CREATE USER ' + QUOTENAME(@n) + N' WITH SID = ' + @sidHex + N', TYPE = E;';
        EXEC (@sql);
    END;
    SET @sql = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@n) + N';';
    EXEC (@sql);
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
        cmd.Parameters.AddWithValue("@clientId", clientId);
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
