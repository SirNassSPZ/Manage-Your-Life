using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace DeuxiemeCerveau.Api.Auth;

/// <summary>
/// Middleware d'authentification (§8) : exige un jeton Bearer Entra ID valide sur chaque appel HTTP.
/// Exemptions : quand l'authentification est désactivée (local/tests), la fonction « ping » (sonde
/// publique), et les requêtes CORS preflight (OPTIONS). Un jeton absent ou invalide court-circuite
/// en 401 sans exécuter la fonction.
/// </summary>
public sealed class MiddlewareAuth(OptionsAuth options, ValidateurJeton validateur) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext contexte, FunctionExecutionDelegate suite)
    {
        var http = contexte.GetHttpContext();

        // Non-HTTP, authentification désactivée, sonde ping, ou preflight CORS → pas de contrôle.
        if (http is null || !options.Activee
            || string.Equals(contexte.FunctionDefinition.Name, "ping", StringComparison.OrdinalIgnoreCase)
            || HttpMethods.IsOptions(http.Request.Method))
        {
            await suite(contexte);
            return;
        }

        var entete = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(entete) || !entete.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await Refuser(http, "jeton_absent", "En-tête Authorization: Bearer manquant.");
            return;
        }

        var jeton = entete["Bearer ".Length..].Trim();
        var principal = await ValiderSansLever(jeton, contexte.CancellationToken);
        if (principal is null)
        {
            await Refuser(http, "jeton_invalide", "Jeton Bearer invalide ou expiré.");
            return;
        }

        http.User = principal;
        await suite(contexte);
    }

    private async Task<System.Security.Claims.ClaimsPrincipal?> ValiderSansLever(
        string jeton, CancellationToken annulation)
    {
        try
        {
            return await validateur.ValiderAsync(jeton, annulation);
        }
        catch
        {
            return null; // métadonnées indisponibles, jeton malformé… : refus sans divulguer le détail
        }
    }

    private static async Task Refuser(HttpContext http, string code, string message)
    {
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.Headers.WWWAuthenticate = "Bearer";
        await http.Response.WriteAsJsonAsync(new { erreur = code, message });
    }
}
