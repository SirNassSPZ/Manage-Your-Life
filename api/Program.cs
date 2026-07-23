using Microsoft.Extensions.Hosting;

// Hôte Functions .NET 8 isolé (§8, plan Consommation), intégration ASP.NET Core :
// point de départ des routes du contrat d'API (Étape 3). Le cœur métier reste isolé (§4).
var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .Build();

host.Run();
