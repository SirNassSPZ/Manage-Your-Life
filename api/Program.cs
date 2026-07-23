using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Hôte Functions .NET 8 isolé (§8, plan Consommation), intégration ASP.NET Core.
// L'API est la fine couche d'adaptateurs autour du cœur (§4) : elle câble les implémentations
// concrètes (magasin, horloge) que le cœur ne connaît que par interface (règle 4).
var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IHorloge, HorlogeSysteme>();

        // Incrément 3a : magasin mémoire (jamais déployé — données non partagées, D-012).
        // Incrément 3b : remplacé par MagasinSynchroSql (Azure SQL), même interface.
        services.AddSingleton<IMagasinSynchro, MagasinSynchroMemoire>();
        services.AddSingleton<IMagasinAppareils, MagasinAppareilsMemoire>();

        services.AddSingleton<ServiceApi>();
    })
    .Build();

host.Run();
