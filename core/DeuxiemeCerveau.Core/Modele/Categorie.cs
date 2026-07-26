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

    /// <summary>
    /// Rang d'affichage choisi par l'utilisateur (§3.3, v3.3) — <b>facultatif</b> : absent, le
    /// classement se fait par nom. Ajout additif (migration 004, règle 18) : la sémantique de la
    /// catégorie et son rôle de calendrier restent inchangés.
    /// </summary>
    public int? Ordre { get; set; }

    /// <summary>
    /// Pictogramme court (§3.3, v3.3) — <b>facultatif</b>. Limité à
    /// <see cref="Validation.ValidateurEntites.IconeLongueurMax"/> caractères, largeur de la
    /// colonne <c>icone NVARCHAR(16)</c> du §9.
    /// </summary>
    public string? Icone { get; set; }
}

/// <summary>Catégories de départ livrées (§3.3).</summary>
public static class CategoriesDeDepart
{
    public static readonly IReadOnlyList<string> Noms =
        ["école", "santé", "psychologie", "sport", "productivité", "justice"];
}
