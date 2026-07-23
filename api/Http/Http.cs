using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Synchro;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DeuxiemeCerveau.Api.Http;

/// <summary>Entrées/sorties JSON canoniques (D-007) pour les endpoints §8 : snake_case, dates UTC « Z ».</summary>
public static class Http
{
    public static async Task<T> Lire<T>(HttpRequest requete)
    {
        using var lecteur = new StreamReader(requete.Body);
        var corps = await lecteur.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(corps))
            throw new System.Text.Json.JsonException("Corps de requête vide.");
        return SerialisationCanonique.Deserialiser<T>(corps);
    }

    /// <summary>Réponse JSON sérialisée avec les options canoniques (pas le sérialiseur de l'hôte).</summary>
    public static IActionResult Json<T>(T valeur, int statut = StatusCodes.Status200OK)
        => new ContentResult
        {
            Content = SerialisationCanonique.Serialiser(valeur),
            ContentType = "application/json; charset=utf-8",
            StatusCode = statut,
        };

    public static IActionResult Erreur(int statut, string code, string message)
        => new ContentResult
        {
            Content = SerialisationCanonique.Serialiser(new { erreur = code, message }),
            ContentType = "application/json; charset=utf-8",
            StatusCode = statut,
        };

    /// <summary>Lot invalide (§6.2.2) → 422 avec la liste des erreurs par change_id.</summary>
    public static IActionResult LotInvalide(ErreurLotInvalide erreur)
        => new ContentResult
        {
            Content = SerialisationCanonique.Serialiser(new
            {
                erreur = "lot_invalide",
                message = erreur.Message,
                changements = erreur.Erreurs.Select(e => new ErreurChangementDto(
                    e.ChangeId,
                    e.Erreurs.Select(v => new ErreurDetailDto(v.Champ, v.Code, v.Message)).ToList())),
            }),
            ContentType = "application/json; charset=utf-8",
            StatusCode = StatusCodes.Status422UnprocessableEntity,
        };
}
