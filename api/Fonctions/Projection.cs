using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.Core.Modele;
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

    /// <summary>
    /// GET /projection/confrontation?montant_centimes={n}&amp;mois_cible={AAAA-MM}&amp;mois={12} (§8).
    /// <para>
    /// Confronte un montant au budget projeté (§5.1bis). <b>GET, et c'est délibéré</b> : rien
    /// n'est écrit, la réponse ne dépend que des paramètres et de l'état lu (règle 9).
    /// </para>
    /// </summary>
    [Function("projection_confrontation")]
    public IActionResult Confrontation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "projection/confrontation")] HttpRequest requete)
    {
        if (!requete.Query.TryGetValue("montant_centimes", out var brutMontant)
            || !long.TryParse(brutMontant, out var montant) || montant <= 0)
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide",
                "montant_centimes obligatoire, entier strictement positif (centimes, règle 5).");

        if (!requete.Query.TryGetValue("mois_cible", out var brutCible))
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide",
                "mois_cible obligatoire, au format AAAA-MM.");

        MoisCalendaire moisCible;
        try
        {
            moisCible = MoisCalendaire.Analyser(brutCible!);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide", ex.Message);
        }

        var mois = 12;
        if (requete.Query.TryGetValue("mois", out var v) && int.TryParse(v, out var n))
            mois = n;
        if (mois is < 1 || mois > CalculateurProjection.NombreMoisMax)
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide",
                $"mois entre 1 et {CalculateurProjection.NombreMoisMax}.");

        try
        {
            return HttpAide.Json(service.Confronter(montant, moisCible, mois));
        }
        catch (SoldeReferenceAbsent ex)
        {
            return HttpAide.Erreur(StatusCodes.Status409Conflict, "solde_reference_absent", ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // Mois cible hors horizon : c'est une demande mal formée, pas une panne du serveur.
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide", ex.Message);
        }
        catch (Exception ex)
        {
            journal.LogError(ex, "Erreur inattendue dans projection/confrontation");
            return HttpAide.Erreur(StatusCodes.Status500InternalServerError, "erreur_interne", ex.ToString());
        }
    }
}
