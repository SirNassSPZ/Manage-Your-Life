using System.Text.RegularExpressions;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Core.Validation;

/// <summary>Validation des entités de classement : catégories (§3.3), projets (§3.2), budgets (§3.6), pièces jointes (§7).</summary>
public static partial class ValidateurEntites
{
    public const int NomCategorieLongueurMax = 100;
    public const int NomProjetLongueurMax = 150;
    public const int NomBudgetLongueurMax = 100;
    public const int NomFichierLongueurMax = 255;

    /// <summary>
    /// Largeur de la colonne <c>icone NVARCHAR(16)</c> (§9, migration 004). Comptée en unités
    /// UTF-16 des deux côtés — <c>string.Length</c> et <c>NVARCHAR</c> comptent la même chose —,
    /// donc un pictogramme hors du plan de base (une émoji, 2 unités) est mesuré ici exactement
    /// comme la base le mesurera : refus explicite plutôt que troncature silencieuse à l'INSERT.
    /// </summary>
    public const int IconeLongueurMax = 16;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex FormatCouleur();

    public static IReadOnlyList<ErreurValidation> Valider(Categorie c)
    {
        var erreurs = new List<ErreurValidation>();
        if (c.Id == Guid.Empty)
            erreurs.Add(new("id", "id_manquant", "Identifiant obligatoire."));
        ValiderNom(erreurs, c.Nom, NomCategorieLongueurMax);
        ValiderCouleur(erreurs, c.Couleur);
        // « ordre » et « icone » sont facultatifs (§3.3) : leur absence n'est jamais une erreur.
        // Aucune contrainte de signe ni d'unicité sur « ordre » — la spec n'en pose pas, et un
        // rang dupliqué se départage par le nom, comme une catégorie sans rang.
        if (c.Icone is { Length: > IconeLongueurMax })
            erreurs.Add(new("icone", "icone_trop_longue",
                $"Pictogramme limité à {IconeLongueurMax} caractères (§9)."));
        erreurs.AddRange(ValidateurAudit.Valider(c));
        return erreurs;
    }

    public static IReadOnlyList<ErreurValidation> Valider(Projet p)
    {
        var erreurs = new List<ErreurValidation>();
        if (p.Id == Guid.Empty)
            erreurs.Add(new("id", "id_manquant", "Identifiant obligatoire."));
        ValiderNom(erreurs, p.Nom, NomProjetLongueurMax);
        ValiderCouleur(erreurs, p.Couleur);
        erreurs.AddRange(ValidateurAudit.Valider(p));
        return erreurs;
    }

    public static IReadOnlyList<ErreurValidation> Valider(Budget b)
    {
        var erreurs = new List<ErreurValidation>();
        if (b.Id == Guid.Empty)
            erreurs.Add(new("id", "id_manquant", "Identifiant obligatoire."));
        ValiderNom(erreurs, b.Nom, NomBudgetLongueurMax);
        ValiderCouleur(erreurs, b.Couleur);
        if (b.MontantPeriodeCentimes < 0)
            erreurs.Add(new("montant_periode_centimes", "montant_negatif",
                "Le plafond d'un budget doit être positif ou nul (centimes entiers)."));
        erreurs.AddRange(ValidateurAudit.Valider(b));
        return erreurs;
    }

    public static IReadOnlyList<ErreurValidation> Valider(PieceJointe p)
    {
        var erreurs = new List<ErreurValidation>();
        if (p.Id == Guid.Empty)
            erreurs.Add(new("id", "id_manquant", "Identifiant obligatoire."));
        if (p.ElementId == Guid.Empty)
            erreurs.Add(new("element_id", "element_manquant", "Une pièce jointe référence un Élément (§7)."));
        if (string.IsNullOrWhiteSpace(p.NomFichier))
            erreurs.Add(new("nom_fichier", "nom_manquant", "Nom de fichier obligatoire."));
        else if (p.NomFichier.Length > NomFichierLongueurMax)
            erreurs.Add(new("nom_fichier", "nom_trop_long", $"Nom de fichier limité à {NomFichierLongueurMax} caractères."));
        if (p.TailleOctets <= 0)
            erreurs.Add(new("taille_octets", "taille_invalide", "Taille de fichier invalide."));
        else if (p.TailleOctets > PieceJointe.TailleMaxOctets)
            erreurs.Add(new("taille_octets", "taille_depassee", "Limite de 25 Mo par pièce (§7)."));
        erreurs.AddRange(ValidateurAudit.Valider(p));
        return erreurs;
    }

    public static IReadOnlyList<ErreurValidation> Valider(ReglageSolde r)
    {
        var erreurs = new List<ErreurValidation>();
        if (r.Id != ReglageSolde.IdSoldeReference)
            erreurs.Add(new("id", "id_reglage_invalide",
                "L'identifiant du réglage solde de référence est déterministe (D-006)."));
        if (r.SoldeReferenceDate == default)
            erreurs.Add(new("solde_reference_date", "date_manquante", "Date du solde de référence obligatoire (§3.4)."));
        erreurs.AddRange(ValidateurAudit.Valider(r));
        return erreurs;
    }

    private static void ValiderNom(List<ErreurValidation> erreurs, string nom, int longueurMax)
    {
        if (string.IsNullOrWhiteSpace(nom))
            erreurs.Add(new("nom", "nom_manquant", "Nom obligatoire."));
        else if (nom.Length > longueurMax)
            erreurs.Add(new("nom", "nom_trop_long", $"Nom limité à {longueurMax} caractères."));
    }

    private static void ValiderCouleur(List<ErreurValidation> erreurs, string couleur)
    {
        if (!FormatCouleur().IsMatch(couleur))
            erreurs.Add(new("couleur", "couleur_invalide", $"Couleur invalide : « {couleur} » (attendu #RRGGBB)."));
    }
}
