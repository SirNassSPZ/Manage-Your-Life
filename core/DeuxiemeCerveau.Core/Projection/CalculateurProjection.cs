using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Recurrence;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.Core.Projection;

/// <summary>Requête de projection : le mois courant est fourni par l'appelant (API) — le cœur est pur et testable.</summary>
public sealed record RequeteProjection(
    MoisCalendaire PremierMois,
    int NombreMois,
    SoldeReference Solde,
    IReadOnlyList<Element> Elements);

/// <summary>Le solde de référence (§3.4) : sans point de départ, aucune projection n'est possible.</summary>
public sealed record SoldeReference(long Centimes, DateOnly Date);

/// <summary>
/// Un mois projeté : ouverture, entrées, sorties, clôture (§8). Soldes null quand le mois précède
/// la date de référence (avant_reference, D-004). Découvert = clôture négative (§5.1 point 5).
/// </summary>
public sealed record MoisProjete(
    int Annee,
    int Mois,
    long? OuvertureCentimes,
    long EntreesCentimes,
    long SortiesCentimes,
    long? ClotureCentimes,
    bool Decouvert,
    bool AvantReference);

/// <summary>
/// Budget projeté — algorithme officiel (§5.1, NON NÉGOCIABLE). Vit dans l'API et uniquement ici ;
/// calculé à la lecture, jamais stocké (règle 9). Précisions d'implémentation : D-004.
/// </summary>
public static class CalculateurProjection
{
    public const int NombreMoisMax = 120;

    public static IReadOnlyList<MoisProjete> Calculer(RequeteProjection requete)
    {
        if (requete.NombreMois is < 1 or > NombreMoisMax)
            throw new ArgumentOutOfRangeException(nameof(requete),
                $"Nombre de mois entre 1 et {NombreMoisMax}.");

        // Point de départ (§5.1.2) : le solde de référence à sa date — instant = ce jour à 00:00 UTC,
        // occurrences comptées à partir de cet instant inclus (D-004).
        var instantReference = new DateTimeOffset(
            requete.Solde.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var moisReference = MoisCalendaire.Depuis(requete.Solde.Date);
        var dernierMois = requete.PremierMois.AjouterMois(requete.NombreMois - 1);

        // Fenêtre d'expansion (§5.1.3) : de la date de référence à la fin de l'horizon.
        // Borne UTC large (les occurrences sont ensuite filtrées par mois local) : 1er jour du mois
        // suivant l'horizon + 2 jours de marge (tout offset réel est dans ±14 h).
        var finHorizonUtc = new DateTimeOffset(
            new DateTime(dernierMois.Annee, dernierMois.Mois, 1, 0, 0, 0, DateTimeKind.Utc))
            .AddMonths(1).AddDays(2);

        var flux = new Dictionary<MoisCalendaire, (long Entrees, long Sorties)>();

        foreach (var element in requete.Elements)
        {
            // Exclusions (§5.1.3, D-004) : annulés, supprimés (corbeille), financiers sans date ou sans montant.
            if (!element.EstFinancier || element.Supprime || element.Statut == StatutElement.Annule)
                continue;
            if (element.DateDebut is not { } dateDebut || element.MontantCentimes is not { } montant)
                continue;

            var fuseau = element.Fuseau is { Length: > 0 } id && FuseauxIana.Resoudre(id) is { } tz
                ? tz
                : TimeZoneInfo.Utc;
            var sens = element.Sens ?? (element.Type == TypeElement.Revenu ? Sens.Entree : Sens.Sortie);

            IEnumerable<Occurrence> occurrences = element.Recurrence is { Length: > 0 } rrule
                ? ExpanseurRecurrence.Expanser(RegleRecurrence.Analyser(rrule), dateDebut, fuseau, finHorizonUtc)
                : [ExpanseurRecurrence.OccurrenceUnique(dateDebut, fuseau)];

            foreach (var occurrence in occurrences)
            {
                if (occurrence.Utc < instantReference)
                    continue; // déjà contenu dans le solde de référence (§5.1.3)

                // Rattachement au mois calendaire local — ce que l'utilisateur voit (D-004).
                var mois = MoisCalendaire.Depuis(occurrence.Locale);
                if (mois > dernierMois)
                    continue;
                if (mois < moisReference)
                    mois = moisReference; // cas limite fuseaux très à l'ouest (D-004)

                var (entrees, sorties) = flux.GetValueOrDefault(mois);
                if (sens == Sens.Entree)
                    entrees += montant;
                else
                    sorties += montant;
                flux[mois] = (entrees, sorties);
            }
        }

        // Cascade mensuelle (§5.1.4) depuis le mois de la date de référence — le report de déficit
        // est automatique par construction (§5.1.5).
        var resultat = new List<MoisProjete>(requete.NombreMois);
        var solde = requete.Solde.Centimes;
        var mois2 = moisReference;
        var soldesAffiches = new Dictionary<MoisCalendaire, (long Ouverture, long Cloture)>();
        while (mois2 <= dernierMois)
        {
            var (entrees, sorties) = flux.GetValueOrDefault(mois2);
            var ouverture = solde;
            solde = solde + entrees - sorties;
            if (mois2 >= requete.PremierMois)
                soldesAffiches[mois2] = (ouverture, solde);
            mois2 = mois2.AjouterMois(1);
        }

        for (var i = 0; i < requete.NombreMois; i++)
        {
            var mois = requete.PremierMois.AjouterMois(i);
            if (mois < moisReference)
            {
                // Aucune projection possible avant le point de départ (§3.4, D-004).
                resultat.Add(new MoisProjete(mois.Annee, mois.Mois,
                    OuvertureCentimes: null, EntreesCentimes: 0, SortiesCentimes: 0,
                    ClotureCentimes: null, Decouvert: false, AvantReference: true));
            }
            else
            {
                var (entrees, sorties) = flux.GetValueOrDefault(mois);
                var (ouverture, cloture) = soldesAffiches[mois];
                resultat.Add(new MoisProjete(mois.Annee, mois.Mois,
                    ouverture, entrees, sorties, cloture,
                    Decouvert: cloture < 0, AvantReference: false));
            }
        }

        return resultat;
    }
}
