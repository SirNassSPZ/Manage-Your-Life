using DeuxiemeCerveau.App.Fichiers;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Validation;

namespace DeuxiemeCerveau.App.Services;

/// <summary>
/// Pièces jointes côté app (§7) — local-first : « joindre » met le fichier en cache IMMÉDIATEMENT et
/// crée ses métadonnées (qui se synchronisent comme toute entité), sans réseau. L'envoi effectif du
/// binaire vers Blob (via URL SAS) se fait ensuite en tâche de fond, avec confirmation. La lecture
/// télécharge à la demande puis met en cache. Limite 25 Mo (§7).
/// </summary>
/// <param name="porte">
/// Sérialise les touches à la base sans englober les appels réseau (<see cref="IPorteDonnees"/>) :
/// l'envoi d'une pièce alterne base et réseau exactement comme la synchro. Absente, aucune
/// sérialisation — le cas d'un test, seul sur sa base.
/// </param>
public sealed class ServicePiecesJointes(
    DepotLocal depot, ServiceSaisie saisie, IStockageFichiersLocal cache, IClientApi api, ITransfertBlob blob,
    IPorteDonnees? porte = null)
{
    private readonly IPorteDonnees _porte = porte ?? PorteOuverte.Instance;

    /// <summary>Joint un fichier à un Élément : cache local immédiat + métadonnées (confirme = false).</summary>
    public ResultatSaisie Joindre(Guid elementId, string nomFichier, byte[] contenu)
    {
        if (contenu.LongLength <= 0 || contenu.LongLength > PieceJointe.TailleMaxOctets)
            return ResultatSaisie.Rejete([new ErreurValidation("taille_octets", "taille_depassee",
                "Limite de 25 Mo par pièce (§7).")]);

        var pieceId = Guid.NewGuid();
        cache.Ecrire(pieceId, contenu); // §7 : enregistrement local immédiat (filet 1)
        var piece = new PieceJointe
        {
            Id = pieceId,
            ElementId = elementId,
            NomFichier = nomFichier,
            TailleOctets = contenu.LongLength,
            BlobPath = $"{elementId:D}/{pieceId:D}", // schéma déterministe, miroir de l'API (D-016)
            Confirme = false,
        };
        var resultat = _porte.Franchir(() => saisie.Enregistrer(piece, EntiteSynchro.PieceJointe)); // → synchro
        if (!resultat.Reussi)
            cache.Supprimer(pieceId);
        return resultat;
    }

    /// <summary>
    /// Envoie en tâche de fond les pièces non confirmées présentes dans le cache local : URL SAS →
    /// téléversement direct → confirmation → marquage confirmé (qui se synchronise). Réessayable :
    /// une pièce déjà confirmée est ignorée, un envoi coupé repart au prochain appel.
    /// </summary>
    public async Task EnvoyerEnAttente(CancellationToken jeton = default)
    {
        // La liste est prise en UNE fois, porte refermée : la parcourir en la tenant ouverte
        // bloquerait l'interface pendant tous les téléversements (filet 1).
        var aEnvoyer = _porte.Franchir(() => depot.Enumerer(EntiteSynchro.PieceJointe)
            .Select(etat => SerialisationCanonique.Deserialiser<PieceJointe>(etat.PayloadCanonique))
            .Where(piece => !piece.Confirme && !piece.Supprime)
            .ToList());

        foreach (var piece in aEnvoyer)
        {
            var contenu = cache.Lire(piece.Id);
            if (contenu is null)
                continue; // créée sur un autre appareil : rien à envoyer d'ici

            var envoi = await api.PreparerEnvoiPiece(piece.ElementId, piece.TailleOctets, piece.Id, jeton);
            await blob.Televerser(envoi.UploadUrl, contenu, jeton);
            await api.ConfirmerPiece(envoi.BlobPath, jeton);

            piece.Confirme = true;
            _porte.Franchir(() => saisie.Enregistrer(piece, EntiteSynchro.PieceJointe)); // confirmé → synchro
        }
    }

    /// <summary>
    /// Renvoie le contenu d'une pièce : depuis le cache si présent, sinon télécharge via URL SAS puis
    /// met en cache. <c>null</c> si la pièce est inconnue en local (métadonnées pas encore tirées).
    /// </summary>
    public async Task<byte[]?> Ouvrir(Guid pieceId, CancellationToken jeton = default)
    {
        if (cache.Lire(pieceId) is { } local)
            return local;
        if (_porte.Franchir(() => depot.Obtenir(EntiteSynchro.PieceJointe, pieceId)) is null)
            return null;
        var lecture = await api.UrlLecturePiece(pieceId, jeton);
        var contenu = await blob.Telecharger(lecture.DownloadUrl, jeton);
        cache.Ecrire(pieceId, contenu); // §7 : mise en cache locale après téléchargement
        return contenu;
    }
}
