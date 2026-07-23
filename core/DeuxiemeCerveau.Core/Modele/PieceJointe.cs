namespace DeuxiemeCerveau.Core.Modele;

/// <summary>
/// Métadonnées d'une pièce jointe (§7). Le fichier vit dans le stockage d'objets (adaptateur) ;
/// le cœur ne connaît que les métadonnées. Soft delete aligné sur l'Élément parent.
/// </summary>
public sealed class PieceJointe : EntiteSynchronisee
{
    public const long TailleMaxOctets = 25L * 1024 * 1024; // 25 Mo (§7)

    public Guid ElementId { get; set; }

    public string NomFichier { get; set; } = string.Empty;

    public long TailleOctets { get; set; }

    /// <summary>Chemin dans le stockage d'objets — opaque pour le cœur.</summary>
    public string BlobPath { get; set; } = string.Empty;

    /// <summary>Vrai une fois le téléversement confirmé (§7).</summary>
    public bool Confirme { get; set; }
}
