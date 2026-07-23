using System.Data.Common;
using DeuxiemeCerveau.Api.Auth;
using DeuxiemeCerveau.Api.Persistence;
using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.Core.Migrations;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Hôte Functions .NET 8 isolé (§8, plan Consommation), intégration ASP.NET Core.
// L'API est la fine couche d'adaptateurs autour du cœur (§4) : elle câble les implémentations
// concrètes (magasin, horloge) que le cœur ne connaît que par interface (règle 4).

var fqdn = Environment.GetEnvironmentVariable("SQL_SERVER_FQDN");
var baseDonnees = Environment.GetEnvironmentVariable("SQL_DATABASE");
var identiteClientId = Environment.GetEnvironmentVariable("SQL_IDENTITY_CLIENT_ID");
var modeSql = !string.IsNullOrWhiteSpace(fqdn) && !string.IsNullOrWhiteSpace(baseDonnees);

// Stockage des pièces jointes (§7) : la chaîne du runtime porte la clé de compte (injectée par Bicep,
// jamais dans git — règle 16), ce qui permet de signer des URL SAS sans attribution de rôle RBAC.
var chaineStockage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
var conteneurPieces = Environment.GetEnvironmentVariable("BLOB_CONTAINER") ?? "pieces-jointes";

// Chaîne de connexion Azure SQL par identité managée (aucun secret ; §8, règle 16). Quand une
// identité ATTRIBUÉE est fournie, on la cible par son client_id ; sinon identité par défaut.
string ChaineSql(int timeout)
{
    var b = new SqlConnectionStringBuilder
    {
        DataSource = $"tcp:{fqdn},1433",
        InitialCatalog = baseDonnees,
        Encrypt = true,
        ConnectTimeout = timeout,
        Authentication = string.IsNullOrWhiteSpace(identiteClientId)
            ? SqlAuthenticationMethod.ActiveDirectoryDefault
            : SqlAuthenticationMethod.ActiveDirectoryManagedIdentity,
    };
    if (!string.IsNullOrWhiteSpace(identiteClientId))
        b.UserID = identiteClientId; // cible l'identité attribuée par son client_id
    return b.ConnectionString;
}

var optionsAuth = OptionsAuth.DepuisEnvironnement();

var builder = new HostBuilder().ConfigureFunctionsWebApplication(worker =>
{
    // Authentification Entra ID sur chaque appel HTTP (§8) — intégrée dès maintenant (Étape 3).
    // Inactive tant que AUTH_ACTIVEE ≠ true : le middleware laisse tout passer (local, ping).
    worker.UseMiddleware<MiddlewareAuth>();
});

builder.ConfigureServices(services =>
{
    services.AddSingleton<IHorloge, HorlogeSysteme>();

    // Pièces jointes (§7) : vrai Blob Storage en production (URL SAS signées par la clé du runtime),
    // fausse implémentation mémoire en local/tests — jamais déployée (données non partagées, D-012).
    if (modeSql && !string.IsNullOrWhiteSpace(chaineStockage))
        services.AddSingleton<IStockagePieces>(_ => new StockagePiecesBlob(chaineStockage, conteneurPieces));
    else
        services.AddSingleton<IStockagePieces, StockagePiecesMemoire>();

    services.AddSingleton(optionsAuth);
    services.AddSingleton(optionsAuth.Activee
        ? ValidateurJeton.DepuisOptions(optionsAuth)
        : new ValidateurJeton(new Microsoft.IdentityModel.Tokens.TokenValidationParameters()));

    if (modeSql)
    {
        // Production : Azure SQL par identité managée attribuée (aucun secret ; §8, règle 16).
        var chaine = ChaineSql(60); // reprise du serverless : quelques secondes au premier appel (§10.1)
        DbConnection Fabrique() => new SqlConnection(chaine);
        services.AddSingleton<Func<DbConnection>>(Fabrique);
        // Le magasin SQL porte l'état de transaction : une instance par invocation (D-012).
        services.AddScoped<IMagasinSynchro>(sp => new MagasinSynchroSql(Fabrique, DialecteSql.AzureSql));
        services.AddScoped<IMagasinAppareils>(sp =>
            new MagasinAppareilsSql(Fabrique, sp.GetRequiredService<IHorloge>()));
        services.AddScoped<ServiceApi>();
    }
    else
    {
        // Local / tests : magasin mémoire (jamais déployé — données non partagées, D-012).
        services.AddSingleton<IMagasinSynchro, MagasinSynchroMemoire>();
        services.AddSingleton<IMagasinAppareils, MagasinAppareilsMemoire>();
        services.AddSingleton<ServiceApi>();
    }
});

var host = builder.Build();

// Migrations appliquées au démarrage (§9, règle 18) — additives, à l'identique partout.
if (modeSql)
    AppliquerMigrations(host, ChaineSql(120)); // premier appel : reprise du serverless en pause

host.Run();
return;

static void AppliquerMigrations(IHost host, string chaine)
{
    var journal = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

    // Retry léger : la base serverless peut être en pause au premier appel (§10.1).
    for (var tentative = 1; ; tentative++)
    {
        try
        {
            using var connexion = new SqlConnection(chaine);
            connexion.Open();
            var appliquees = new CibleMigrationSql(connexion, DialecteSql.AzureSql).AppliquerAuDemarrage();
            journal.LogInformation("Migrations appliquées : {N}", appliquees.Count);
            return;
        }
        catch (Exception ex) when (tentative < 5)
        {
            journal.LogWarning(ex, "Reprise SQL (tentative {T}) — nouvel essai…", tentative);
            Thread.Sleep(TimeSpan.FromSeconds(5 * tentative));
        }
    }
}
