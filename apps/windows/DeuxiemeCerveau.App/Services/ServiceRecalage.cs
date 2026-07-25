namespace DeuxiemeCerveau.App.Services;

/// <summary>
/// Ce que l'app montre AVANT de valider un recalage (§3.4) : d'où l'on part, où l'on va, et l'écart —
/// exposé platement, sans jugement (reco #9). <c>EcartCentimes</c> est <c>null</c> au tout premier
/// réglage (il n'y a pas encore de point de départ à corriger).
/// </summary>
public sealed record EtatRecalage(
    long? AncienCentimes,
    DateOnly? AncienneDate,
    long NouveauCentimes,
    DateOnly NouvelleDate,
    long? EcartCentimes);

/// <summary>
/// Recalage du solde de référence (feuille de route Palier 1 rang 6 / reco #9) — Étape 4f.
///
/// <para>La spec en fait <b>l'unique geste de correction</b> quand la réalité et la projection s'écartent
/// (§3.4) : plutôt que de corriger ligne à ligne, l'utilisateur redonne son solde réel et tout se
/// reprojette à partir de là. Ce n'est donc pas un aveu d'échec mais un entretien normal — d'où l'écart
/// affiché <b>tel quel</b>, avant validation, pour que le geste soit compris et non subi.</para>
///
/// Passe par la saisie ordinaire (locale d'abord puis synchro, « dernier recalage gagne », §3.4) : c'est
/// exactement le même chemin que l'onboarding, il n'existe qu'un seul réglage `solde_reference` (D-006).
/// </summary>
public sealed class ServiceRecalage(ServiceAujourdhui accueil, ServiceDemarrage demarrage)
{
    /// <summary>Prépare l'aperçu du recalage (sans rien écrire) : ancien solde, nouveau, écart.</summary>
    public EtatRecalage Preparer(long soldeReelCentimes, DateOnly date)
    {
        var actuel = accueil.SoldeDeReference();
        return new EtatRecalage(
            actuel?.Centimes,
            actuel?.Date,
            soldeReelCentimes,
            date,
            actuel is null ? null : soldeReelCentimes - actuel.Centimes);
    }

    /// <summary>Applique le recalage — écriture locale immédiate, synchronisée ensuite (§6.1, filet 1).</summary>
    public ResultatSaisie Appliquer(long soldeReelCentimes, DateOnly date)
        => demarrage.DefinirSoldeReference(soldeReelCentimes, date);
}
