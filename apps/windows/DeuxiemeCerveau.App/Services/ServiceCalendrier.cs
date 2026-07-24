using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Recurrence;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.App.Services;

/// <summary>Une occurrence à afficher au calendrier (§5.4).</summary>
public sealed record OccurrenceCalendrier(
    Guid ElementId, string Titre, TypeElement Type, DateTimeOffset InstantUtc, long? MontantCentimes, Sens? Sens);

/// <summary>
/// Calendrier unifié (§5.4) : rendez-vous ET échéances financières datées (factures, paiements, revenus
/// datés). Les RRULE sont développées <b>pour l'affichage seulement</b> (§4 — le calcul budgétaire, lui,
/// reste au serveur, §5.1). Chaque catégorie est un filtre affichable/masquable. Local, sans réseau.
/// </summary>
public sealed class ServiceCalendrier(DepotLocal depot)
{
    /// <summary>
    /// Occurrences datées dans la fenêtre [<paramref name="debut"/>, <paramref name="fin"/>], triées.
    /// Si <paramref name="categoriesVisibles"/> est fourni, un Élément catégorisé n'apparaît que s'il a
    /// au moins une catégorie visible ; un Élément sans catégorie reste toujours visible.
    /// </summary>
    public IReadOnlyList<OccurrenceCalendrier> Occurrences(
        DateTimeOffset debut, DateTimeOffset fin, IReadOnlySet<Guid>? categoriesVisibles = null)
    {
        var resultat = new List<OccurrenceCalendrier>();
        foreach (var etat in depot.Enumerer(EntiteSynchro.Element))
        {
            var e = SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);
            if (e.Supprime || e.DateDebut is null || e.Statut == StatutElement.Annule)
                continue;
            if (categoriesVisibles is not null && e.Categories.Count > 0 && !e.Categories.Any(categoriesVisibles.Contains))
                continue;

            var fuseau = FuseauxIana.Resoudre(e.Fuseau ?? "UTC") ?? TimeZoneInfo.Utc;
            foreach (var instant in Expanser(e, fuseau, debut, fin))
                resultat.Add(new OccurrenceCalendrier(e.Id, e.Titre, e.Type, instant, e.MontantCentimes, e.Sens));
        }
        return resultat.OrderBy(o => o.InstantUtc).ToList();
    }

    private static IEnumerable<DateTimeOffset> Expanser(Element e, TimeZoneInfo fuseau, DateTimeOffset debut, DateTimeOffset fin)
    {
        var dtstart = e.DateDebut!.Value;
        if (string.IsNullOrWhiteSpace(e.Recurrence))
        {
            if (dtstart >= debut && dtstart <= fin)
                yield return dtstart;
            yield break;
        }
        var regle = RegleRecurrence.Analyser(e.Recurrence);
        foreach (var occ in ExpanseurRecurrence.Expanser(regle, dtstart, fuseau, fin))
            if (occ.Utc >= debut)
                yield return occ.Utc;
    }
}
