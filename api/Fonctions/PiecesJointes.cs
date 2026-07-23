using System.Text.Json;
using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using HttpAide = DeuxiemeCerveau.Api.Http.Http;

namespace DeuxiemeCerveau.Api.Fonctions;

/// <summary>
/// Endpoints des pièces jointes (§7, §8) — adaptateurs minces (règle 4). Ils courtent uniquement des
/// URL SAS temporaires : le binaire transite en direct entre le client et Blob Storage, jamais par
/// l'API. Les métadonnées (PieceJointe) transitent, elles, par la synchro (§6.2) comme toute entité.
/// </summary>
public sealed class PiecesJointes(ServiceApi service, ILogger<PiecesJointes> journal)
{
    /// <summary>GET /attachments/upload-url?element_id=&amp;taille_octets=&amp;attachment_id= (§8).</summary>
    [Function("attachments_upload_url")]
    public IActionResult UrlEnvoi(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attachments/upload-url")] HttpRequest requete)
    {
        try
        {
            if (!LireGuid(requete, "element_id", out var elementId) || elementId == Guid.Empty)
                return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide", "element_id (GUID) obligatoire.");
            if (!LireLong(requete, "taille_octets", out var taille))
                return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide", "taille_octets (entier) obligatoire.");
            LireGuid(requete, "attachment_id", out var pieceId); // optionnel : réutilisé lors d'un réessai
            var pieceIdOpt = pieceId == Guid.Empty ? (Guid?)null : pieceId;
            return HttpAide.Json(service.PreparerEnvoiPiece(elementId, taille, pieceIdOpt));
        }
        catch (PieceTropVolumineuse ex)
        {
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "taille_invalide", ex.Message);
        }
        catch (Exception ex)
        {
            journal.LogError(ex, "Erreur inattendue dans attachments/upload-url");
            return HttpAide.Erreur(StatusCodes.Status500InternalServerError, "erreur_interne", ex.ToString());
        }
    }

    /// <summary>POST /attachments/confirm (§8) : vérifie que le binaire est bien arrivé dans le stockage.</summary>
    [Function("attachments_confirm")]
    public async Task<IActionResult> Confirmer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "attachments/confirm")] HttpRequest requete)
    {
        DemandeConfirmation demande;
        try
        {
            demande = await HttpAide.Lire<DemandeConfirmation>(requete);
        }
        catch (JsonException ex)
        {
            return HttpAide.Erreur(StatusCodes.Status400BadRequest, "corps_invalide", ex.Message);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(demande.BlobPath))
                return HttpAide.Erreur(StatusCodes.Status400BadRequest, "champ_manquant", "blob_path obligatoire.");
            return HttpAide.Json(service.ConfirmerEnvoiPiece(demande.BlobPath));
        }
        catch (TeleversementAbsent ex)
        {
            return HttpAide.Erreur(StatusCodes.Status409Conflict, "televersement_absent", ex.Message);
        }
        catch (Exception ex)
        {
            journal.LogError(ex, "Erreur inattendue dans attachments/confirm");
            return HttpAide.Erreur(StatusCodes.Status500InternalServerError, "erreur_interne", ex.ToString());
        }
    }

    /// <summary>GET /attachments/{id}/download-url (§8) : URL SAS de lecture d'après les métadonnées synchronisées.</summary>
    [Function("attachments_download_url")]
    public IActionResult UrlLecture(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attachments/{id}/download-url")] HttpRequest requete,
        string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var pieceId))
                return HttpAide.Erreur(StatusCodes.Status400BadRequest, "parametre_invalide", "id (GUID) invalide.");
            return HttpAide.Json(service.UrlLecturePiece(pieceId));
        }
        catch (PieceIntrouvable ex)
        {
            return HttpAide.Erreur(StatusCodes.Status404NotFound, "piece_introuvable", ex.Message);
        }
        catch (Exception ex)
        {
            journal.LogError(ex, "Erreur inattendue dans attachments/download-url");
            return HttpAide.Erreur(StatusCodes.Status500InternalServerError, "erreur_interne", ex.ToString());
        }
    }

    private static bool LireGuid(HttpRequest requete, string cle, out Guid valeur)
    {
        valeur = Guid.Empty;
        return requete.Query.TryGetValue(cle, out var v) && Guid.TryParse(v, out valeur);
    }

    private static bool LireLong(HttpRequest requete, string cle, out long valeur)
    {
        valeur = 0;
        return requete.Query.TryGetValue(cle, out var v) && long.TryParse(v, out valeur);
    }
}
