using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Windows.Core.Reseau;

public sealed class ClientApiHttp : IClientApi
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private string? _jetonBearer;

    public ClientApiHttp(HttpClient http, string baseUrl, string? jetonBearer = null)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _jetonBearer = jetonBearer;
    }

    public void DefinirJetonBearer(string jeton)
    {
        _jetonBearer = jeton;
    }

    private HttpRequestMessage CreerRequete(HttpMethod methode, string chemin)
    {
        var requete = new HttpRequestMessage(methode, $"{_baseUrl}{chemin}");
        requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_jetonBearer))
        {
            requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jetonBearer);
        }
        return requete;
    }

    public async Task<Guid> EnregistrerAppareilAsync(string nom, string plateforme, CancellationToken cancellationToken = default)
    {
        using var requete = CreerRequete(HttpMethod.Post, "/api/devices/register");
        var payload = JsonSerializer.Serialize(new DemandeEnregistrementAppareilDto { Nom = nom, Plateforme = plateforme });
        requete.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var reponse = await _http.SendAsync(requete, cancellationToken);
        reponse.EnsureSuccessStatusCode();
        var json = await reponse.Content.ReadAsStringAsync(cancellationToken);
        var dto = SerialisationCanonique.Deserialiser<ReponseEnregistrementAppareilDto>(json);
        return dto.AppareilId;
    }

    public async Task<ReponsePush> PousserAsync(LotPush lot, CancellationToken cancellationToken = default)
    {
        using var requete = CreerRequete(HttpMethod.Post, "/api/sync/push");
        var payload = SerialisationCanonique.Serialiser(lot);
        requete.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var reponse = await _http.SendAsync(requete, cancellationToken);
        reponse.EnsureSuccessStatusCode();
        var json = await reponse.Content.ReadAsStringAsync(cancellationToken);
        return SerialisationCanonique.Deserialiser<ReponsePush>(json);
    }

    public async Task<PagePull> TirerAsync(long since, int limite = 5000, CancellationToken cancellationToken = default)
    {
        using var requete = CreerRequete(HttpMethod.Get, $"/api/sync/pull?since={since}&limite={limite}");
        using var reponse = await _http.SendAsync(requete, cancellationToken);
        reponse.EnsureSuccessStatusCode();
        var json = await reponse.Content.ReadAsStringAsync(cancellationToken);
        return SerialisationCanonique.Deserialiser<PagePull>(json);
    }

    public async Task RecalerSoldeAsync(DemandeRecalageSolde demande, CancellationToken cancellationToken = default)
    {
        using var requete = CreerRequete(HttpMethod.Put, "/api/settings/solde-reference");
        var payload = SerialisationCanonique.Serialiser(demande);
        requete.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var reponse = await _http.SendAsync(requete, cancellationToken);
        reponse.EnsureSuccessStatusCode();
    }

    public async Task<ReponsePurge> PurgerAsync(LotPurge lot, CancellationToken cancellationToken = default)
    {
        using var requete = CreerRequete(HttpMethod.Post, "/api/purge");
        var payload = SerialisationCanonique.Serialiser(lot);
        requete.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var reponse = await _http.SendAsync(requete, cancellationToken);
        reponse.EnsureSuccessStatusCode();
        var json = await reponse.Content.ReadAsStringAsync(cancellationToken);
        return SerialisationCanonique.Deserialiser<ReponsePurge>(json);
    }
}
