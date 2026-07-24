namespace DeuxiemeCerveau.App.Fichiers;

/// <summary>
/// Cache local des fichiers de pièces jointes (§7) — le binaire vit ici en attendant/après l'envoi vers
/// Blob Storage. Clé = identifiant de la pièce. Alimente aussi l'export (§5.7). Adaptateur : disque en
/// production (coquille WinUI), mémoire pour les tests.
/// </summary>
public interface IStockageFichiersLocal
{
    void Ecrire(Guid pieceId, byte[] contenu);

    byte[]? Lire(Guid pieceId);

    bool Existe(Guid pieceId);

    void Supprimer(Guid pieceId);
}
