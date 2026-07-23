namespace DeuxiemeCerveau.Core.Modele;

/// <summary>
/// Le contrat central (§3) : facture, paiement, revenu, tâche, rendez-vous, envie, note — tout est un Élément.
/// Les deux applications doivent représenter l'Élément avec exactement ces champs et cette sémantique.
/// </summary>
public sealed class Element : EntiteSynchronisee
{
    public TypeElement Type { get; set; }

    /// <summary>Texte court, ≤ 300 caractères.</summary>
    public string Titre { get; set; } = string.Empty;

    public string? Description { get; set; }

    // ----- Temps (§3.5) -----

    /// <summary>UTC. Pour <c>journee_entiere</c> : minuit local du jour, converti en UTC (D-009).</summary>
    public DateTimeOffset? DateDebut { get; set; }

    /// <summary>UTC, pour les plages.</summary>
    public DateTimeOffset? DateFin { get; set; }

    /// <summary>Identifiant IANA (ex. « Europe/Paris »), obligatoire dès qu'une date est présente.</summary>
    public string? Fuseau { get; set; }

    public bool JourneeEntiere { get; set; }

    /// <summary>Réservé aux envies (§3.1) : déclenche le rappel intelligent plutôt qu'une alerte fixe (V2).</summary>
    public bool DateApproximative { get; set; }

    /// <summary>RRULE (RFC 5545), expansée dans le fuseau de l'Élément. Aucun format maison (règle 6).</summary>
    public string? Recurrence { get; set; }

    // ----- Argent (uniquement facture, paiement, revenu ; règle 5 : centimes entiers) -----

    public long? MontantCentimes { get; set; }

    /// <summary>ISO 4217 (« EUR » par défaut côté saisie).</summary>
    public string? Devise { get; set; }

    public Sens? Sens { get; set; }

    // ----- Classement -----

    public List<Guid> Categories { get; set; } = new();

    public Guid? ProjetId { get; set; }

    /// <summary>Uniquement pour les sorties (§3.6) ; une dépense pèse sur un seul budget au plus.</summary>
    public Guid? BudgetId { get; set; }

    // ----- Tâches -----

    public bool EstObligatoire { get; set; }

    public int? ScorePoints { get; set; }

    public Priorite? Priorite { get; set; }

    public int? OrdreManuel { get; set; }

    // ----- Pièces jointes et rappels -----

    public List<Guid> PiecesJointes { get; set; } = new();

    public List<Rappel> Rappels { get; set; } = new();

    // ----- Statut (table par type, §3.1) -----

    public StatutElement Statut { get; set; }

    /// <summary>Vrai pour les types porteurs d'argent (§3.1). Dérivé — jamais sérialisé.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EstFinancier => Type is TypeElement.Facture or TypeElement.Paiement or TypeElement.Revenu;
}

/// <summary>Rappel (§3.1) : « { type: relatif | absolu, minutes_avant?: entier, date?: UTC } ».</summary>
public sealed class Rappel
{
    public TypeRappel Type { get; set; }

    public int? MinutesAvant { get; set; }

    public DateTimeOffset? Date { get; set; }
}
