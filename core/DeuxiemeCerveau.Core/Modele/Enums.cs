namespace DeuxiemeCerveau.Core.Modele;

/// <summary>Types d'Élément (§3.1). Représentation JSON : snake_case minuscule (« facture », « rendezvous », …).</summary>
public enum TypeElement
{
    Facture,
    Paiement,
    Revenu,
    Tache,
    Rendezvous,
    Envie,
    Note,
}

/// <summary>Sens d'un Élément financier (§3.1) : « entree » (revenu) ou « sortie » (facture, paiement).</summary>
public enum Sens
{
    Entree,
    Sortie,
}

/// <summary>
/// Ensemble des statuts, toutes familles confondues (§3.1). La table des statuts autorisés
/// par type est appliquée par la validation (<see cref="Validation.StatutsAutorises"/>).
/// </summary>
public enum StatutElement
{
    // facture, paiement
    AVenir,
    Paye,
    Annule,
    // revenu
    Attendu,
    Recu,
    // tache
    AFaire,
    Fait,
    Reporte,
    // rendezvous
    Planifie,
    // envie
    Idee,
    Planifiee,
    Faite,
    Abandonnee,
    // note
    Active,
    Archivee,
}

/// <summary>Priorité d'une tâche (§3.1).</summary>
public enum Priorite
{
    Basse,
    Normale,
    Haute,
}

/// <summary>Type de rappel (§3.1) : relatif (minutes avant) ou absolu (date UTC).</summary>
public enum TypeRappel
{
    Relatif,
    Absolu,
}

/// <summary>Origine d'une catégorie (§3.3).</summary>
public enum OrigineCategorie
{
    Transversale,
    Projet,
}

/// <summary>Statut d'un projet (§3.2).</summary>
public enum StatutProjet
{
    Actif,
    EnPause,
    Termine,
}

/// <summary>Statut d'un budget (§3.6).</summary>
public enum StatutBudget
{
    Actif,
    Archive,
}

/// <summary>Période d'un budget (§3.6) : « mensuel » seul en V1/V2.</summary>
public enum PeriodeBudget
{
    Mensuel,
}
