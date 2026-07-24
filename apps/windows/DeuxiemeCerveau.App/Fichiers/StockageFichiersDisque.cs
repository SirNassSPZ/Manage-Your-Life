namespace DeuxiemeCerveau.App.Fichiers;

/// <summary>
/// Cache de fichiers sur disque (production, coquille WinUI). Un fichier par pièce, nommé par son
/// identifiant, sous un dossier de cache fourni par la plateforme (jamais codé en dur, règle 4).
/// </summary>
public sealed class StockageFichiersDisque : IStockageFichiersLocal
{
    private readonly string _dossier;

    public StockageFichiersDisque(string dossier)
    {
        _dossier = dossier;
        Directory.CreateDirectory(_dossier);
    }

    private string Chemin(Guid pieceId) => Path.Combine(_dossier, pieceId.ToString("N"));

    public void Ecrire(Guid pieceId, byte[] contenu) => File.WriteAllBytes(Chemin(pieceId), contenu);

    public byte[]? Lire(Guid pieceId) => File.Exists(Chemin(pieceId)) ? File.ReadAllBytes(Chemin(pieceId)) : null;

    public bool Existe(Guid pieceId) => File.Exists(Chemin(pieceId));

    public void Supprimer(Guid pieceId)
    {
        var chemin = Chemin(pieceId);
        if (File.Exists(chemin))
            File.Delete(chemin);
    }
}
