using System.Net.Http.Headers;
using System.Text;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Synchro;

/// <summary>
/// Implémentation HTTP d'<see cref="IClientApi"/> (§8). Sérialise en JSON canonique (mêmes options que
/// le serveur, D-007). Le <see cref="HttpClient"/> est fourni configuré par la coquille (adresse de
/// base = URL de l'API en config §2 ; en-tête Bearer Entra ID) — aucune adresse ni secret ici (règle 4).
/// </summary>
public sealed class ClientApiHttp(HttpClient http) : IClientApi
{
    public async Task<Guid> EnregistrerAppareil(string nom, string plateforme, CancellationToken jeton = default)
    {
        var corps = SerialisationCanonique.Serialiser(new DemandeEnregistrement(nom, plateforme));
        var reponse = await Envoyer(HttpMethod.Post, "api/devices/register", corps, jeton);
        return SerialisationCanonique.Deserialiser<ReponseEnregistrementClient>(reponse).AppareilId;
    }

    public async Task<ReponsePushClient> Pousser(LotPush lot, CancellationToken jeton = default)
    {
        var reponse = await Envoyer(HttpMethod.Post, "api/sync/push", SerialisationCanonique.Serialiser(lot), jeton);
        return SerialisationCanonique.Deserialiser<ReponsePushClient>(reponse);
    }

    public async Task<ReponsePullClient> Tirer(long depuis, int limite, CancellationToken jeton = default)
    {
        var reponse = await Envoyer(HttpMethod.Get, $"api/sync/pull?since={depuis}&limite={limite}", null, jeton);
        return SerialisationCanonique.Deserialiser<ReponsePullClient>(reponse);
    }

    public async Task<ReponsePurgeClient> Purger(LotPurge lot, CancellationToken jeton = default)
    {
        var reponse = await Envoyer(HttpMethod.Post, "api/purge", SerialisationCanonique.Serialiser(lot), jeton);
        return SerialisationCanonique.Deserialiser<ReponsePurgeClient>(reponse);
    }

    public async Task<ReponseProjectionClient> Projeter(int mois, CancellationToken jeton = default)
    {
        var reponse = await Envoyer(HttpMethod.Get, $"api/projection/budget?mois={mois}", null, jeton);
        return SerialisationCanonique.Deserialiser<ReponseProjectionClient>(reponse);
    }

    public async Task<ReponseConfrontationClient> Confronter(
        long montantCentimes, string moisCible, int mois, CancellationToken jeton = default)
    {
        var chemin = $"api/projection/confrontation?montant_centimes={montantCentimes}"
                   + $"&mois_cible={Uri.EscapeDataString(moisCible)}&mois={mois}";
        var reponse = await Envoyer(HttpMethod.Get, chemin, null, jeton);
        return SerialisationCanonique.Deserialiser<ReponseConfrontationClient>(reponse);
    }

    public async Task<ReponseUrlEnvoiClient> PreparerEnvoiPiece(Guid elementId, long tailleOctets, Guid attachmentId, CancellationToken jeton = default)
    {
        var chemin = $"api/attachments/upload-url?element_id={elementId}&taille_octets={tailleOctets}&attachment_id={attachmentId}";
        var reponse = await Envoyer(HttpMethod.Get, chemin, null, jeton);
        return SerialisationCanonique.Deserialiser<ReponseUrlEnvoiClient>(reponse);
    }

    public async Task<ReponseConfirmationClient> ConfirmerPiece(string blobPath, CancellationToken jeton = default)
    {
        var corps = SerialisationCanonique.Serialiser(new DemandeConfirmationClient(blobPath));
        var reponse = await Envoyer(HttpMethod.Post, "api/attachments/confirm", corps, jeton);
        return SerialisationCanonique.Deserialiser<ReponseConfirmationClient>(reponse);
    }

    public async Task<ReponseUrlLectureClient> UrlLecturePiece(Guid attachmentId, CancellationToken jeton = default)
    {
        var reponse = await Envoyer(HttpMethod.Get, $"api/attachments/{attachmentId}/download-url", null, jeton);
        return SerialisationCanonique.Deserialiser<ReponseUrlLectureClient>(reponse);
    }

    private async Task<string> Envoyer(HttpMethod methode, string chemin, string? corps, CancellationToken jeton)
    {
        using var requete = new HttpRequestMessage(methode, chemin);
        if (corps is not null)
            requete.Content = new StringContent(corps, Encoding.UTF8, "application/json");
        requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var reponse = await http.SendAsync(requete, jeton);
        var texte = await reponse.Content.ReadAsStringAsync(jeton);
        if (!reponse.IsSuccessStatusCode)
            throw new ErreurSynchro((int)reponse.StatusCode, texte);
        return texte;
    }

    private sealed record DemandeEnregistrement(string Nom, string Plateforme);
}

/// <summary>Échec d'un appel de synchro (statut HTTP ≥ 400). La synchro réessaiera plus tard (§6.2).</summary>
public sealed class ErreurSynchro(int statut, string corps)
    : Exception($"Réponse serveur {statut} : {corps}")
{
    public int Statut { get; } = statut;
}
