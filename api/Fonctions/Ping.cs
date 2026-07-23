using DeuxiemeCerveau.Core.Migrations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DeuxiemeCerveau.Api.Fonctions;

/// <summary>
/// Fonction « ping » (Étape 2) : preuve que l'API répond en ligne. Elle traverse la frontière
/// adaptateur → cœur (référence <see cref="ListeMigrations"/>) sans qu'aucune dépendance Azure ne
/// remonte dans le cœur (règle 4). Le contrat complet §8 arrive à l'Étape 3.
/// </summary>
public sealed class Ping
{
    [Function("ping")]
    public IActionResult Executer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")] HttpRequest requete)
        => new OkObjectResult(new
        {
            service = "deuxieme-cerveau-api",
            statut = "en ligne",
            version_schema = ListeMigrations.Toutes.Count,
            horodatage = DateTimeOffset.UtcNow,
        });
}
