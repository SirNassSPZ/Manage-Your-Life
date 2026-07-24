using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Services;

/// <summary>Le solde de référence (§3.4) tel que lu localement, avec sa date de recalage.</summary>
public sealed record SoldeReference(long Centimes, DateOnly Date);

/// <summary>Les occurrences datées d'un jour, pour la bande « à venir » de l'accueil.</summary>
public sealed record JourAgenda(DateOnly Jour, IReadOnlyList<OccurrenceCalendrier> Occurrences);

/// <summary>
/// Accueil « Aujourd'hui » (feuille de route, Palier 1 rang 1 / reco #10) : une vue calme et locale qui
/// ouvre l'app sur le jour et les jours à venir, plutôt que sur la dette.
///
/// <para><b>Frontière §4 respectée.</b> Ce service NE calcule PAS le budget projeté (§5.1) — cet
/// algorithme « vit dans l'API et uniquement là ». Il se contente d'<b>assembler</b> deux choses déjà
/// autorisées côté client : le <b>solde de référence</b> (réglage §3.4, synchronisé) et les
/// <b>occurrences datées développées pour l'affichage</b> (§4, via <see cref="ServiceCalendrier"/>). Le
/// chiffre « clôture projetée fin de mois » viendra, lui, de la projection serveur, pas d'ici.</para>
///
/// Local-first, sans réseau (règle 4) ; ne stocke aucune donnée dérivée (règle 9).
/// </summary>
public sealed class ServiceAujourdhui(DepotLocal depot, ServiceCalendrier calendrier)
{
    /// <summary>
    /// Le solde de référence local (§3.4), ou <c>null</c> si l'utilisateur ne l'a jamais posé — auquel cas
    /// l'accueil invite à le faire (onboarding, reco #1).
    /// </summary>
    public SoldeReference? SoldeDeReference()
    {
        var etat = depot.Enumerer(EntiteSynchro.Reglage)
            .FirstOrDefault(e => e.Id == ReglageSolde.IdSoldeReference && !e.Supprime);
        if (etat is null)
            return null;
        var r = SerialisationCanonique.Deserialiser<ReglageSolde>(etat.PayloadCanonique);
        return new SoldeReference(r.SoldeReferenceCentimes, r.SoldeReferenceDate);
    }

    /// <summary>
    /// Les occurrences datées d'aujourd'hui, triées par instant. « Aujourd'hui » = le jour civil de
    /// <paramref name="maintenant"/> dans son propre décalage (l'app passe l'heure locale de l'appareil).
    /// </summary>
    public IReadOnlyList<OccurrenceCalendrier> Aujourdhui(DateTimeOffset maintenant)
    {
        var debut = DebutDuJour(maintenant);
        return calendrier.Occurrences(debut, debut.AddDays(1).AddTicks(-1));
    }

    /// <summary>
    /// Les <paramref name="jours"/> prochains jours (aujourd'hui inclus), groupés par jour civil local et
    /// triés — la bande « à venir » de l'accueil (7 jours par défaut, reco #10). Les jours sans occurrence
    /// sont omis. Le regroupement se fait dans le décalage de <paramref name="maintenant"/> (heure de
    /// l'appareil), cohérent avec l'affichage.
    /// </summary>
    public IReadOnlyList<JourAgenda> ProchainsJours(DateTimeOffset maintenant, int jours = 7)
    {
        if (jours < 1)
            jours = 1;
        var debut = DebutDuJour(maintenant);
        var fin = debut.AddDays(jours).AddTicks(-1);
        var offset = maintenant.Offset;
        return calendrier.Occurrences(debut, fin)
            .GroupBy(o => DateOnly.FromDateTime(o.InstantUtc.ToOffset(offset).DateTime))
            .OrderBy(g => g.Key)
            .Select(g => new JourAgenda(g.Key, g.OrderBy(o => o.InstantUtc).ToList()))
            .ToList();
    }

    /// <summary>Minuit du jour civil de <paramref name="maintenant"/>, dans son décalage.</summary>
    private static DateTimeOffset DebutDuJour(DateTimeOffset maintenant)
        => new(maintenant.Year, maintenant.Month, maintenant.Day, 0, 0, 0, maintenant.Offset);
}
