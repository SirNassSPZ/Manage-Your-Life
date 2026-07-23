using System.Text.RegularExpressions;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Recurrence;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.Core.Validation;

/// <summary>
/// Validation d'un Élément (§3.1, §3.6, D-009) — vit dans l'API, écrite une seule fois (règle 3).
/// </summary>
public static partial class ValidateurElement
{
    public const int TitreLongueurMax = 300;
    public const int RecurrenceLongueurMax = 500;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex FormatDevise();

    public static IReadOnlyList<ErreurValidation> Valider(Element e)
    {
        var erreurs = new List<ErreurValidation>();

        // ----- Identité et contenu -----
        if (e.Id == Guid.Empty)
            erreurs.Add(new("id", "id_manquant", "Identifiant obligatoire (UUID généré sur l'appareil)."));
        if (string.IsNullOrWhiteSpace(e.Titre))
            erreurs.Add(new("titre", "titre_manquant", "Titre obligatoire."));
        else if (e.Titre.Length > TitreLongueurMax)
            erreurs.Add(new("titre", "titre_trop_long", $"Titre limité à {TitreLongueurMax} caractères."));

        // ----- Statut : table par type (§3.1, règle 8) -----
        if (!StatutsAutorises.EstAutorise(e.Type, e.Statut))
            erreurs.Add(new("statut", "statut_interdit",
                $"Statut « {e.Statut} » interdit pour le type « {e.Type} » (§3.1)."));

        // ----- Temps (§3.5) -----
        var aUneDate = e.DateDebut is not null || e.DateFin is not null;
        if (aUneDate && string.IsNullOrEmpty(e.Fuseau))
            erreurs.Add(new("fuseau", "fuseau_manquant", "Fuseau IANA obligatoire dès qu'une date est présente."));
        if (!aUneDate && !string.IsNullOrEmpty(e.Fuseau))
            erreurs.Add(new("fuseau", "fuseau_sans_date", "Fuseau présent sans aucune date (D-009)."));
        if (!string.IsNullOrEmpty(e.Fuseau) && !FuseauxIana.EstValide(e.Fuseau))
            erreurs.Add(new("fuseau", "fuseau_invalide", $"Fuseau IANA invalide : « {e.Fuseau} »."));

        if (e.DateFin is not null && e.DateDebut is null)
            erreurs.Add(new("date_fin", "date_fin_sans_debut", "date_fin exige date_debut."));
        if (e.DateFin is not null && e.DateDebut is not null && e.DateFin < e.DateDebut)
            erreurs.Add(new("date_fin", "dates_incoherentes", "date_fin antérieure à date_debut."));
        if (e.JourneeEntiere && e.DateDebut is null)
            erreurs.Add(new("journee_entiere", "journee_sans_date", "journee_entiere exige date_debut."));

        if (e.DateApproximative && e.Type != TypeElement.Envie)
            erreurs.Add(new("date_approximative", "date_approximative_reservee",
                "date_approximative est réservée aux envies (§3.1)."));

        // ----- Récurrence (règle 6) -----
        if (e.Recurrence is not null)
        {
            if (e.Recurrence.Length > RecurrenceLongueurMax)
                erreurs.Add(new("recurrence", "recurrence_trop_longue",
                    $"RRULE limitée à {RecurrenceLongueurMax} caractères (§9)."));
            if (e.DateDebut is null)
                erreurs.Add(new("recurrence", "recurrence_sans_date", "Une récurrence exige date_debut (DTSTART)."));
            try
            {
                RegleRecurrence.Analyser(e.Recurrence);
            }
            catch (ErreurRecurrence erreur)
            {
                erreurs.Add(new("recurrence", "recurrence_invalide", erreur.Message));
            }
        }

        // ----- Argent (§3.1, règle 5) -----
        if (e.EstFinancier)
        {
            if (e.MontantCentimes is null)
                erreurs.Add(new("montant_centimes", "montant_manquant",
                    "Montant (centimes entiers) obligatoire pour un Élément financier."));
            else if (e.MontantCentimes < 0)
                erreurs.Add(new("montant_centimes", "montant_negatif",
                    "Montant négatif interdit — le sens porte la direction."));

            if (e.Devise is null)
                erreurs.Add(new("devise", "devise_manquante", "Devise ISO 4217 obligatoire."));
            else if (!FormatDevise().IsMatch(e.Devise))
                erreurs.Add(new("devise", "devise_invalide", $"Devise invalide : « {e.Devise} » (ISO 4217, ex. EUR)."));

            var sensAttendu = e.Type == TypeElement.Revenu ? Sens.Entree : Sens.Sortie;
            if (e.Sens is null)
                erreurs.Add(new("sens", "sens_manquant", "Sens obligatoire pour un Élément financier."));
            else if (e.Sens != sensAttendu)
                erreurs.Add(new("sens", "sens_incoherent",
                    $"Sens « {e.Sens} » incohérent avec le type « {e.Type} » (§3.1)."));
        }
        else
        {
            if (e.MontantCentimes is not null)
                erreurs.Add(new("montant_centimes", "montant_interdit",
                    $"Montant interdit sur le type « {e.Type} » (§3.1 : argent uniquement facture, paiement, revenu)."));
            if (e.Devise is not null)
                erreurs.Add(new("devise", "devise_interdite", $"Devise interdite sur le type « {e.Type} »."));
            if (e.Sens is not null)
                erreurs.Add(new("sens", "sens_interdit", $"Sens interdit sur le type « {e.Type} »."));
        }

        // ----- budget_id : uniquement sur une sortie (§3.6) -----
        if (e.BudgetId is not null && e.Type is not (TypeElement.Facture or TypeElement.Paiement))
            erreurs.Add(new("budget_id", "budget_interdit",
                "budget_id n'est accepté que sur un Élément financier de sens « sortie » (§3.6)."));

        // ----- Champs de tâche : réservés au type tache (D-009) -----
        if (e.Type != TypeElement.Tache)
        {
            if (e.EstObligatoire)
                erreurs.Add(new("est_obligatoire", "champ_reserve_taches", "est_obligatoire est réservé aux tâches."));
            if (e.ScorePoints is not null)
                erreurs.Add(new("score_points", "champ_reserve_taches", "score_points est réservé aux tâches."));
            if (e.Priorite is not null)
                erreurs.Add(new("priorite", "champ_reserve_taches", "priorite est réservée aux tâches."));
            if (e.OrdreManuel is not null)
                erreurs.Add(new("ordre_manuel", "champ_reserve_taches", "ordre_manuel est réservé aux tâches."));
        }
        else if (e.ScorePoints is < 0)
        {
            erreurs.Add(new("score_points", "score_negatif", "score_points doit être positif ou nul."));
        }

        // ----- Rappels (§3.1) -----
        for (var i = 0; i < e.Rappels.Count; i++)
        {
            var rappel = e.Rappels[i];
            var champ = $"rappels[{i}]";
            switch (rappel.Type)
            {
                case TypeRappel.Relatif:
                    if (rappel.MinutesAvant is null)
                        erreurs.Add(new(champ, "rappel_invalide", "Rappel relatif : minutes_avant obligatoire."));
                    else if (rappel.MinutesAvant < 0)
                        erreurs.Add(new(champ, "rappel_invalide", "minutes_avant doit être positif ou nul."));
                    if (rappel.Date is not null)
                        erreurs.Add(new(champ, "rappel_invalide", "Rappel relatif : date interdite."));
                    break;
                case TypeRappel.Absolu:
                    if (rappel.Date is null)
                        erreurs.Add(new(champ, "rappel_invalide", "Rappel absolu : date (UTC) obligatoire."));
                    if (rappel.MinutesAvant is not null)
                        erreurs.Add(new(champ, "rappel_invalide", "Rappel absolu : minutes_avant interdit."));
                    break;
            }
        }

        // ----- Classement : pas de doublons -----
        if (e.Categories.Count != e.Categories.Distinct().Count())
            erreurs.Add(new("categories", "categories_dupliquees", "Références de catégories dupliquées."));
        if (e.PiecesJointes.Count != e.PiecesJointes.Distinct().Count())
            erreurs.Add(new("pieces_jointes", "pieces_jointes_dupliquees", "Références de pièces jointes dupliquées."));

        erreurs.AddRange(ValidateurAudit.Valider(e));
        return erreurs;
    }
}
