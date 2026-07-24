namespace DeuxiemeCerveau.App.Fichiers;

/// <summary>
/// Transfert direct du binaire vers/depuis Blob Storage via une URL SAS temporaire (§7) — le fichier
/// ne transite jamais par l'API. Adaptateur : HTTP en production, mémoire pour les tests.
/// </summary>
public interface ITransfertBlob
{
    Task Televerser(string urlSas, byte[] contenu, CancellationToken jeton = default);

    Task<byte[]> Telecharger(string urlSas, CancellationToken jeton = default);
}
