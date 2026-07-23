namespace DeuxiemeCerveau.Api.Persistence;

/// <summary>
/// Fausse implémentation d'<see cref="IStockagePieces"/> pour le local et les tests : ne touche aucun
/// stockage réel, renvoie des URL déterministes et simule la présence du fichier une fois l'envoi
/// préparé. Jamais déployée (le mode SQL branche le vrai Blob Storage, D-012).
/// </summary>
public sealed class StockagePiecesMemoire : IStockagePieces
{
    private const string Racine = "https://stockage.local/pieces-jointes";
    private readonly HashSet<string> _prepares = [];

    public (Uri Url, DateTimeOffset ExpireLe) PreparerEnvoi(string blobPath, TimeSpan duree)
    {
        _prepares.Add(blobPath);
        return (new Uri($"{Racine}/{blobPath}?envoi&sig=faux"), DateTimeOffset.UtcNow.Add(duree));
    }

    public (Uri Url, DateTimeOffset ExpireLe) UrlLecture(string blobPath, TimeSpan duree)
        => (new Uri($"{Racine}/{blobPath}?lecture&sig=faux"), DateTimeOffset.UtcNow.Add(duree));

    public long? TailleSiPresent(string blobPath)
        => _prepares.Contains(blobPath) ? 1024 : null;
}
