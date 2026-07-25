using System.Net;
using System.Net.Http.Headers;

namespace DeuxiemeCerveau.Windows.Services;

/// <summary>
/// Injecte le Bearer Entra sur chaque appel à l'API.
/// <para>
/// C'est ici et nulle part ailleurs : <c>ClientApiHttp</c> ne connaît que son HttpClient (règle 4),
/// et poser l'en-tête une fois au démarrage cesserait de marcher à l'expiration du jeton.
/// </para>
/// </summary>
public sealed class ManipulateurJeton(IFournisseurJeton jetons) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage requete, CancellationToken jeton)
    {
        if (await jetons.ObtenirSilencieux(jeton).ConfigureAwait(false) is { } acces)
            requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acces);

        return await base.SendAsync(requete, jeton).ConfigureAwait(false);
    }
}

/// <summary>
/// Réessaie les échecs transitoires. Motivé par la reprise du SQL serverless : la base se met en
/// pause après inactivité et le premier appel qui la réveille peut prendre quelques secondes
/// (spec §10.1). L'interface n'attend de toute façon jamais le serveur (filet 1).
/// </summary>
public sealed class ManipulateurReessai : DelegatingHandler
{
    private static readonly TimeSpan[] Attentes =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage requete, CancellationToken jeton)
    {
        for (var essai = 0; ; essai++)
        {
            try
            {
                var reponse = await base.SendAsync(requete, jeton).ConfigureAwait(false);
                if (essai >= Attentes.Length || !EstTransitoire(reponse.StatusCode)) return reponse;
                reponse.Dispose();
            }
            catch (HttpRequestException) when (essai < Attentes.Length)
            {
                // Réseau coupé : on retente, puis on laisse remonter (la saisie reste locale).
            }
            catch (TaskCanceledException) when (essai < Attentes.Length && !jeton.IsCancellationRequested)
            {
                // Délai dépassé — typiquement le réveil de la base.
            }

            await Task.Delay(Attentes[essai], jeton).ConfigureAwait(false);
        }
    }

    private static bool EstTransitoire(HttpStatusCode statut) =>
        statut is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}
