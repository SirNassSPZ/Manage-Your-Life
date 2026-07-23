using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.Core.Projection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using HttpAide = DeuxiemeCerveau.Api.Http.Http;

namespace DeuxiemeCerveau.Api.Fonctions;

/// <summary>Endpoint de projection budgétaire (§5.1, §8) — le calcul vit dans le cœur (règle 9).</summary>
public sealed class Projection(ServiceApi service, ILogger<Projection> journal)
{
    /// <summary>GET /projection/budget?mois=12 (§8).</summary>
    [Function("projection_budget")]
    public IActionResult Budget(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "projection/budget")] HttpRequest requete)
    {
        var mois = 12;
        if (requete.Query.TryGetValue("mois", out var v) && int.TryParse(v, out var n))
            mois = n;
        if (mois is < 1 || mois > CalculateurProjection.NombreMoisMax)
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide",
                $"mois entre 1 et {CalculateurProjection.NombreMoisMax}.");

        try
        {
            return HttpAide.Json(service.Projeter(mois));
        }
        catch (SoldeReferenceAbsent ex)
        {
            return HttpAide.Erreur(StatusCodes.Status409Conflict, "solde_reference_absent", ex.Message);
        }
        catch (Exception ex)
        {
            journal.LogError(ex, "Erreur inattendue dans projection/budget");
            return HttpAide.Erreur(StatusCodes.Status500InternalServerError, "erreur_interne", ex.ToString());
        }
    }
}
