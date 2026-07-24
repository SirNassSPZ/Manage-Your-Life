using System.Globalization;
using System.Text;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.App.Services;

/// <summary>
/// Saisie rapide segmentée (feuille de route Palier 1 rang 3 / reco #3, D-018 #3) : composer un Élément
/// financier valide à partir d'une ligne à champs séparés (sens, libellé, montant, date, récurrence).
/// PAS de langage naturel (le parsing de texte libre reste V3, §13) — juste des champs pré-typés. Le type
/// et le statut initial se déduisent du sens, pour qu'une saisie en un geste produise le même Élément
/// qu'un formulaire complet, validé ensuite par la saisie ordinaire (§3.1).
/// </summary>
public static class SaisieRapide
{
    public static Element Composer(
        Sens sens, string titre, long montantCentimes, DateTimeOffset premiereEcheance,
        string? recurrence = null, string fuseau = "Europe/Paris")
        => new()
        {
            Type = sens == Sens.Entree ? TypeElement.Revenu : TypeElement.Facture,
            Titre = titre,
            DateDebut = premiereEcheance,
            Fuseau = fuseau,
            Recurrence = string.IsNullOrWhiteSpace(recurrence) ? null : recurrence,
            MontantCentimes = montantCentimes,
            Devise = "EUR",
            Sens = sens,
            Statut = sens == Sens.Entree ? StatutElement.Attendu : StatutElement.AVenir,
        };
}

/// <summary>
/// Suggestion de catégorie (feuille de route Palier 1 rang 3 / reco #4, D-018 #2) : une fonction PURE qui
/// propose une catégorie <b>existante</b> à partir d'un libellé. <b>Facultative et modifiable</b> — sans
/// correspondance, elle renvoie <c>null</c> et l'Élément reste sans catégorie (ce qui est valide, §3.6).
/// Elle n'impose rien et ne crée aucune catégorie : les catégories restent celles du §3.3.
/// </summary>
public static class SuggestionCategorie
{
    // Motif normalisé présent dans le libellé → nom canonique de catégorie visé.
    private static readonly (string Motif, string Cible)[] Regles =
    [
        ("loyer", "logement"), ("appartement", "logement"), ("logement", "logement"),
        ("electricite", "charges"), ("edf", "charges"), ("gaz", "charges"), ("eau", "charges"), ("chauffage", "charges"),
        ("internet", "charges"), ("box", "charges"), ("fibre", "charges"), ("telephone", "charges"), ("mobile", "charges"), ("forfait", "charges"),
        ("course", "alimentation"), ("supermarche", "alimentation"), ("restaurant", "alimentation"), ("resto", "alimentation"), ("boulangerie", "alimentation"),
        ("essence", "transport"), ("carburant", "transport"), ("train", "transport"), ("metro", "transport"), ("bus", "transport"), ("sncf", "transport"), ("transport", "transport"),
        ("assurance", "assurance"), ("mutuelle", "assurance"),
        ("salaire", "revenu"), ("paie", "revenu"), ("prime", "revenu"),
        ("abonnement", "loisirs"), ("netflix", "loisirs"), ("spotify", "loisirs"), ("loisir", "loisirs"),
    ];

    /// <summary>La catégorie existante suggérée pour ce libellé, ou <c>null</c> si aucune ne s'impose.</summary>
    public static Guid? Suggerer(string libelle, IReadOnlyList<Categorie> categories)
    {
        if (string.IsNullOrWhiteSpace(libelle) || categories.Count == 0)
            return null;
        var l = Normaliser(libelle);

        // 1) Correspondance directe : le libellé évoque une catégorie existante par son nom (≥ 3 lettres).
        foreach (var c in categories.Where(c => !c.Supprime))
        {
            var n = Normaliser(c.Nom);
            if (n.Length >= 3 && l.Contains(n))
                return c.Id;
        }
        // 2) Correspondance par mot-clé → nom canonique → catégorie existante portant ce nom.
        foreach (var (motif, cible) in Regles)
        {
            if (!l.Contains(motif))
                continue;
            var trouve = categories.FirstOrDefault(c => !c.Supprime && Normaliser(c.Nom).Contains(cible));
            if (trouve is not null)
                return trouve.Id;
        }
        return null;
    }

    /// <summary>Minuscule, sans accents — pour comparer libellés et noms de catégories de façon robuste.</summary>
    private static string Normaliser(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString();
    }
}
