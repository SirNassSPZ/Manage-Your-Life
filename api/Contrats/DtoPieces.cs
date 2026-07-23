using System.Text.Json.Serialization;

namespace DeuxiemeCerveau.Api.Contrats;

// DTOs des pièces jointes (§7, §8). Ils courtent l'accès au binaire ; les métadonnées (PieceJointe)
// transitent, elles, par la synchro (§6.2) comme toute entité. JSON canonique snake_case (D-007).

/// <summary>Réponse de <c>GET /attachments/upload-url</c> (§8) : où et comment téléverser le binaire.</summary>
public sealed record ReponseUrlEnvoiDto(
    [property: JsonPropertyName("attachment_id")] Guid AttachmentId,
    [property: JsonPropertyName("blob_path")] string BlobPath,
    [property: JsonPropertyName("upload_url")] string UploadUrl,
    [property: JsonPropertyName("expire_le")] DateTimeOffset ExpireLe);

/// <summary>Corps de <c>POST /attachments/confirm</c> (§8) : confirme un téléversement terminé.</summary>
public sealed record DemandeConfirmation(
    [property: JsonPropertyName("blob_path")] string BlobPath);

public sealed record ReponseConfirmationDto(
    bool Confirme,
    [property: JsonPropertyName("taille_octets")] long TailleOctets);

/// <summary>Réponse de <c>GET /attachments/{id}/download-url</c> (§8) : URL SAS de lecture.</summary>
public sealed record ReponseUrlLectureDto(
    [property: JsonPropertyName("download_url")] string DownloadUrl,
    [property: JsonPropertyName("nom_fichier")] string NomFichier,
    [property: JsonPropertyName("expire_le")] DateTimeOffset ExpireLe);
