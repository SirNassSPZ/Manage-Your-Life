using System.Net.Http.Headers;

namespace DeuxiemeCerveau.App.Fichiers;

/// <summary>
/// Transfert HTTP direct vers Blob Storage (§7). Le téléversement pose l'en-tête
/// <c>x-ms-blob-type: BlockBlob</c> exigé par Azure ; l'URL SAS porte déjà l'autorisation (aucun
/// secret ici, règle 16). Le <see cref="HttpClient"/> est fourni par la coquille.
/// </summary>
public sealed class TransfertBlobHttp(HttpClient http) : ITransfertBlob
{
    public async Task Televerser(string urlSas, byte[] contenu, CancellationToken jeton = default)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Put, urlSas)
        {
            Content = new ByteArrayContent(contenu),
        };
        requete.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        requete.Headers.Add("x-ms-blob-type", "BlockBlob");
        using var reponse = await http.SendAsync(requete, jeton);
        reponse.EnsureSuccessStatusCode();
    }

    public async Task<byte[]> Telecharger(string urlSas, CancellationToken jeton = default)
    {
        using var reponse = await http.GetAsync(urlSas, jeton);
        reponse.EnsureSuccessStatusCode();
        return await reponse.Content.ReadAsByteArrayAsync(jeton);
    }
}
