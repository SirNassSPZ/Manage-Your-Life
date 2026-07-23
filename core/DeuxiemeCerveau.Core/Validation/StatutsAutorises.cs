using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Core.Validation;

/// <summary>
/// La table exhaustive des statuts autorisés par type (§3.1) — à respecter à l'identique
/// dans les deux apps (règle 8). Toute divergence ici est une divergence de contrat.
/// </summary>
public static class StatutsAutorises
{
    public static readonly IReadOnlyDictionary<TypeElement, IReadOnlySet<StatutElement>> ParType =
        new Dictionary<TypeElement, IReadOnlySet<StatutElement>>
        {
            [TypeElement.Facture] = new HashSet<StatutElement>
                { StatutElement.AVenir, StatutElement.Paye, StatutElement.Annule },
            [TypeElement.Paiement] = new HashSet<StatutElement>
                { StatutElement.AVenir, StatutElement.Paye, StatutElement.Annule },
            [TypeElement.Revenu] = new HashSet<StatutElement>
                { StatutElement.Attendu, StatutElement.Recu, StatutElement.Annule },
            [TypeElement.Tache] = new HashSet<StatutElement>
                { StatutElement.AFaire, StatutElement.Fait, StatutElement.Reporte, StatutElement.Annule },
            [TypeElement.Rendezvous] = new HashSet<StatutElement>
                { StatutElement.Planifie, StatutElement.Fait, StatutElement.Annule },
            [TypeElement.Envie] = new HashSet<StatutElement>
                { StatutElement.Idee, StatutElement.Planifiee, StatutElement.Faite, StatutElement.Abandonnee },
            [TypeElement.Note] = new HashSet<StatutElement>
                { StatutElement.Active, StatutElement.Archivee },
        };

    public static bool EstAutorise(TypeElement type, StatutElement statut)
        => ParType.TryGetValue(type, out var autorises) && autorises.Contains(statut);
}
