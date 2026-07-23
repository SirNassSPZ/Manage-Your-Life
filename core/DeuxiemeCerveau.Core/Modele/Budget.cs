namespace DeuxiemeCerveau.Core.Modele;

/// <summary>
/// Budget (§3.6) : enveloppe de dépenses plafonnée par période. Le champ <c>budget_id</c> des Éléments
/// existe dès la V1 (stabilité du schéma) ; la gestion des enveloppes arrive en V2.
/// Le suivi (alloué / dépensé / engagé / reste) est calculé à la lecture par l'API, jamais stocké (règle 9).
/// </summary>
public sealed class Budget : EntiteSynchronisee
{
    public string Nom { get; set; } = string.Empty;

    /// <summary>Format « #RRGGBB ».</summary>
    public string Couleur { get; set; } = string.Empty;

    /// <summary>Plafond par période, en centimes entiers (règle 5).</summary>
    public long MontantPeriodeCentimes { get; set; }

    public PeriodeBudget Periode { get; set; }

    public StatutBudget Statut { get; set; }
}
