using System.Text.Json;
using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.Core.Synchro;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using HttpAide = DeuxiemeCerveau.Api.Http.Http;

namespace DeuxiemeCerveau.Api.Fonctions;

/// <summary>Endpoints de synchronisation (§6.2, §8) — adaptateurs minces autour du cœur (règle 4).</summary>
public sealed class Synchro(ServiceApi service)
{
    /// <summary>POST /devices/register (§8).</summary>
    [Function("devices_register")]
    public async Task<IActionResult> Enregistrer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "devices/register")] HttpRequest requete)
    {
        try
        {
            var demande = await HttpAide.Lire<DemandeEnregistrementAppareil>(requete);
            if (string.IsNullOrWhiteSpace(demande.Nom) || string.IsNullOrWhiteSpace(demande.Plateforme))
                return HttpAide.Erreur(StatusCodes.Status400BadRequest, "champ_manquant", "nom et plateforme obligatoires.");
            return HttpAide.Json(service.EnregistrerAppareil(demande));
        }
        catch (JsonException ex)
        {
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "corps_invalide", ex.Message);
        }
    }

    /// <summary>POST /sync/push (§8) : idempotent, atomique.</summary>
    [Function("sync_push")]
    public async Task<IActionResult> Pousser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sync/push")] HttpRequest requete)
    {
        LotPush lot;
        try
        {
            lot = await HttpAide.Lire<LotPush>(requete);
        }
        catch (JsonException ex)
        {
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "corps_invalide", ex.Message);
        }

        try
        {
            return HttpAide.Json(service.Pousser(lot));
        }
        catch (ErreurLotInvalide erreur)
        {
            return HttpAide.LotInvalide(erreur);
        }
    }

    /// <summary>GET /sync/pull?since={seq}&amp;limite={n} (§8) : reprise par curseur.</summary>
    [Function("sync_pull")]
    public IActionResult Tirer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sync/pull")] HttpRequest requete)
    {
        var depuis = LireLong(requete, "since", 0);
        var limite = (int)LireLong(requete, "limite", ProcesseurPull.LimiteParDefaut);
        if (depuis < 0 || limite is < 1 or > 5000)
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide", "since ≥ 0 et 1 ≤ limite ≤ 5000.");
        return HttpAide.Json(service.Tirer(depuis, limite));
    }

    /// <summary>PUT /settings/solde-reference (§3.4, §8).</summary>
    [Function("settings_solde_reference")]
    public async Task<IActionResult> Recaler(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "settings/solde-reference")] HttpRequest requete)
    {
        try
        {
            var demande = await HttpAide.Lire<DemandeRecalageSolde>(requete);
            return HttpAide.Json(service.Recaler(demande));
        }
        catch (JsonException ex)
        {
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "corps_invalide", ex.Message);
        }
        catch (ErreurLotInvalide erreur)
        {
            return HttpAide.LotInvalide(erreur);
        }
    }

    /// <summary>POST /purge (§5.6, D-010) : purge définitive depuis la corbeille.</summary>
    [Function("purge")]
    public async Task<IActionResult> Purger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "purge")] HttpRequest requete)
    {
        LotPurge lot;
        try
        {
            lot = await HttpAide.Lire<LotPurge>(requete);
        }
        catch (JsonException ex)
        {
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "corps_invalide", ex.Message);
        }

        try
        {
            var reponse = service.Purger(lot);
            return HttpAide.Json(new
            {
                resultats = reponse.Resultats.Select(r => new
                {
                    change_id = r.ChangeId,
                    statut = r.Statut,
                    server_seq = r.ServerSeq,
                    rejoue = r.Rejoue,
                    motif = r.Motif,
                }),
                purges_induites = reponse.PurgesInduites.Select(p => new ChangementInduitDto(
                    p.ChangeId, p.Entite, p.EntiteId, p.ServerSeq)),
            });
        }
        catch (ErreurLotInvalide erreur)
        {
            return HttpAide.LotInvalide(erreur);
        }
    }

    private static long LireLong(HttpRequest requete, string cle, long defaut)
        => requete.Query.TryGetValue(cle, out var v) && long.TryParse(v, out var n) ? n : defaut;
}
