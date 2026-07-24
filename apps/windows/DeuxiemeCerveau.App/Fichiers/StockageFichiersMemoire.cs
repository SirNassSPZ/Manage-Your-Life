namespace DeuxiemeCerveau.App.Fichiers;

/// <summary>Cache de fichiers en mémoire (tests et démarrage) — jamais persistant.</summary>
public sealed class StockageFichiersMemoire : IStockageFichiersLocal
{
    private readonly Dictionary<Guid, byte[]> _fichiers = [];

    public void Ecrire(Guid pieceId, byte[] contenu) => _fichiers[pieceId] = contenu;

    public byte[]? Lire(Guid pieceId) => _fichiers.TryGetValue(pieceId, out var c) ? c : null;

    public bool Existe(Guid pieceId) => _fichiers.ContainsKey(pieceId);

    public void Supprimer(Guid pieceId) => _fichiers.Remove(pieceId);
}
