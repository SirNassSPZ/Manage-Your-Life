namespace DeuxiemeCerveau.Api.Persistence;

/// <summary>
/// Courtage d'URL signées (SAS) vers le stockage d'objets pour les pièces jointes (§7). Adaptateur
/// Azure : le cœur ne connaît que les métadonnées (règle 4). Le binaire ne transite jamais par l'API —
/// le client téléverse et télécharge en direct via ces URL temporaires (§7, jamais de conteneur public).
/// </summary>
public interface IStockagePieces
{
    /// <summary>URL SAS d'écriture (courte durée) pour téléverser un fichier vers <paramref name="blobPath"/>.</summary>
    (Uri Url, DateTimeOffset ExpireLe) PreparerEnvoi(string blobPath, TimeSpan duree);

    /// <summary>URL SAS de lecture (courte durée) pour télécharger <paramref name="blobPath"/>.</summary>
    (Uri Url, DateTimeOffset ExpireLe) UrlLecture(string blobPath, TimeSpan duree);

    /// <summary>Taille du blob s'il est présent (confirmation d'un envoi terminé, §7), sinon <c>null</c>.</summary>
    long? TailleSiPresent(string blobPath);
}
