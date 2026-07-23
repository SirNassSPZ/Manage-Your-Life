using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Core.Validation;

/// <summary>Validation des champs d'audit/synchro communs (§3.1, NON NÉGOCIABLES).</summary>
public static class ValidateurAudit
{
    public static IReadOnlyList<ErreurValidation> Valider(EntiteSynchronisee e)
    {
        var erreurs = new List<ErreurValidation>();

        if (e.Version < 1)
            erreurs.Add(new("version", "version_invalide", "version est un compteur croissant ≥ 1."));
        if (e.AppareilSource == Guid.Empty)
            erreurs.Add(new("appareil_source", "appareil_manquant", "appareil_source obligatoire."));
        if (e.DateCreation == default)
            erreurs.Add(new("date_creation", "date_creation_manquante", "date_creation (UTC) obligatoire."));
        if (e.DateModification == default)
            erreurs.Add(new("date_modification", "date_modification_manquante", "date_modification (UTC) obligatoire."));
        if (e.DateCreation != default && e.DateModification != default && e.DateModification < e.DateCreation)
            erreurs.Add(new("date_modification", "audit_incoherent", "date_modification antérieure à date_creation."));

        if (e.Supprime && e.DateSuppression is null)
            erreurs.Add(new("date_suppression", "suppression_incoherente",
                "supprime = true exige date_suppression (soft delete, filet 2)."));
        if (!e.Supprime && e.DateSuppression is not null)
            erreurs.Add(new("date_suppression", "suppression_incoherente",
                "date_suppression présente sans supprime = true."));

        return erreurs;
    }
}
