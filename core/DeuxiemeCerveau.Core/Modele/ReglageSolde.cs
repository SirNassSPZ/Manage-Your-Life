namespace DeuxiemeCerveau.Core.Modele;

/// <summary>
/// Réglage du solde de référence (§3.4) : le point de départ sans lequel aucune projection n'est possible.
/// Synchronisé comme le reste (dernier recalage gagne, historique conservé au journal) — entité « reglage »
/// du moteur de synchro (D-006), identifiant déterministe <see cref="IdSoldeReference"/>.
/// </summary>
public sealed class ReglageSolde : EntiteSynchronisee
{
    public const string Cle = "solde_reference";

    /// <summary>UUIDv5 déterministe dérivé de la clé « solde_reference » (D-006).</summary>
    public static readonly Guid IdSoldeReference = Synchro.Uuid5.Derive("reglage:" + Cle);

    /// <summary>Centimes entiers ; négatif autorisé (découvert réel, D-004).</summary>
    public long SoldeReferenceCentimes { get; set; }

    /// <summary>Date calendaire (UTC). L'instant de référence est ce jour à 00:00 UTC (D-004).</summary>
    public DateOnly SoldeReferenceDate { get; set; }
}
