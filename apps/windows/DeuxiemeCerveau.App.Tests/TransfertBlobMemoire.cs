using System.Collections.Concurrent;
using DeuxiemeCerveau.App.Fichiers;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Faux Blob Storage partagé entre appareils : range le binaire par chemin blob, extrait de l'URL SAS
/// (« …/pieces-jointes/{blob_path}?… »). Simule le stockage d'objets réel pour les aller-retours.
/// </summary>
public sealed class TransfertBlobMemoire : ITransfertBlob
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    public Task Televerser(string urlSas, byte[] contenu, CancellationToken jeton = default)
    {
        _blobs[Chemin(urlSas)] = contenu;
        return Task.CompletedTask;
    }

    public Task<byte[]> Telecharger(string urlSas, CancellationToken jeton = default)
        => _blobs.TryGetValue(Chemin(urlSas), out var contenu)
            ? Task.FromResult(contenu)
            : throw new InvalidOperationException($"Blob absent : {Chemin(urlSas)}");

    private static string Chemin(string urlSas)
    {
        var chemin = new Uri(urlSas).AbsolutePath; // /pieces-jointes/{element}/{piece}
        const string prefixe = "/pieces-jointes/";
        var i = chemin.IndexOf(prefixe, StringComparison.Ordinal);
        return i >= 0 ? chemin[(i + prefixe.Length)..] : chemin;
    }
}
