namespace DeuxiemeCerveau.Core.Modele;

/// <summary>
/// Catégorie / Calendrier — notion unifiée (§3.3, NON NÉGOCIABLE) : catégorie = label = calendrier.
/// </summary>
public sealed class Categorie : EntiteSynchronisee
{
    public string Nom { get; set; } = string.Empty;

    /// <summary>Format « #RRGGBB ».</summary>
    public string Couleur { get; set; } = string.Empty;

    public OrigineCategorie Origine { get; set; }
}

/// <summary>Catégories de départ livrées (§3.3).</summary>
public static class CategoriesDeDepart
{
    public static readonly IReadOnlyList<string> Noms =
        ["école", "santé", "psychologie", "sport", "productivité", "justice"];
}
