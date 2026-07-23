using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.Core.Recurrence;

/// <summary>Une occurrence : heure murale locale (fuseau de l'Élément) + instant UTC correspondant.</summary>
public readonly record struct Occurrence(DateTime Locale, DateTimeOffset Utc);

/// <summary>
/// Expansion d'une RRULE dans le fuseau de l'Élément (règle 6) : les occurrences sont générées en heure
/// locale (un loyer « le 5 » reste le 5, changements d'heure compris) puis converties en UTC (D-002).
/// Sémantique RFC 5545 : les dates invalides sont sautées ; DTSTART est la première occurrence et
/// compte pour 1 dans COUNT (D-003).
/// </summary>
public static class ExpanseurRecurrence
{
    /// <summary>
    /// Énumère les occurrences dont l'instant UTC est ≤ <paramref name="utcMax"/>,
    /// dans l'ordre chronologique, en partant de DTSTART (= <paramref name="debutUtc"/> convertie
    /// dans <paramref name="fuseau"/>). Le filtrage par borne basse appartient à l'appelant.
    /// </summary>
    public static IEnumerable<Occurrence> Expanser(
        RegleRecurrence regle, DateTimeOffset debutUtc, TimeZoneInfo fuseau, DateTimeOffset utcMax)
    {
        var debutLocal = ConvertisseurFuseau.VersLocale(debutUtc, fuseau);
        // Borne de sûreté sur l'heure locale : tout offset réel est dans ±14 h → 2 jours de marge.
        var localeMax = utcMax.UtcDateTime.AddDays(2);
        var compte = 0;

        foreach (var locale in GenererLocales(regle, debutLocal, localeMax))
        {
            if (locale > localeMax)
                yield break;

            if (regle.JusquaDateLocale is { } dateMax && DateOnly.FromDateTime(locale) > dateMax)
                yield break;

            var utc = ConvertisseurFuseau.VersUtc(locale, fuseau);

            if (regle.JusquaInstantUtc is { } instantMax && utc > instantMax)
                yield break;

            compte++;
            if (regle.Nombre is { } nombre && compte > nombre)
                yield break;

            if (utc > utcMax)
                yield break;

            // L'heure murale émise est l'heure RÉELLE de l'instant : une candidate tombée dans un
            // trou DST (02:30 inexistante) devient l'heure décalée (03:30) — l'occurrence n'est
            // ni perdue ni affichée sur une heure qui n'existe pas (D-002).
            yield return new Occurrence(ConvertisseurFuseau.VersLocale(utc, fuseau), utc);
        }
    }

    /// <summary>Occurrence unique (Élément sans récurrence) — même représentation.</summary>
    public static Occurrence OccurrenceUnique(DateTimeOffset debutUtc, TimeZoneInfo fuseau)
        => new(ConvertisseurFuseau.VersLocale(debutUtc, fuseau), debutUtc);

    // ----- Génération en heure murale locale, ordre strictement croissant -----

    private static IEnumerable<DateTime> GenererLocales(RegleRecurrence regle, DateTime debutLocal, DateTime borneLocale)
    {
        // DTSTART est toujours la première occurrence (D-003).
        yield return debutLocal;

        var suite = regle.Frequence switch
        {
            FrequenceRecurrence.Quotidienne => GenererQuotidiennes(regle, debutLocal, borneLocale),
            FrequenceRecurrence.Hebdomadaire => GenererHebdomadaires(regle, debutLocal, borneLocale),
            FrequenceRecurrence.Mensuelle => GenererMensuelles(regle, debutLocal, borneLocale),
            FrequenceRecurrence.Annuelle => GenererAnnuelles(regle, debutLocal, borneLocale),
            _ => throw new ErreurRecurrence($"Fréquence inconnue : {regle.Frequence}."),
        };

        foreach (var locale in suite)
            if (locale > debutLocal) // DTSTART déjà émis ; jamais de doublon ni de retour en arrière
                yield return locale;
    }

    private static IEnumerable<DateTime> GenererQuotidiennes(RegleRecurrence regle, DateTime debut, DateTime borne)
    {
        for (long k = 1; ; k++)
        {
            var locale = debut.AddDays(k * regle.Intervalle);
            yield return locale;
            if (locale > borne)
                yield break;
        }
    }

    private static IEnumerable<DateTime> GenererHebdomadaires(RegleRecurrence regle, DateTime debut, DateTime borne)
    {
        var heure = debut.TimeOfDay;
        var jours = regle.JoursSemaine.Count > 0
            ? regle.JoursSemaine.Select(j => j.Jour).Distinct()
            : [debut.DayOfWeek];
        var decalages = jours
            .Select(j => DecalageDansSemaine(j, regle.DebutSemaine))
            .OrderBy(d => d)
            .ToArray();

        // Semaine 0 = semaine de DTSTART (ancrage RFC) ; on avance de « intervalle » semaines.
        var origine = debut.Date.AddDays(-DecalageDansSemaine(debut.DayOfWeek, regle.DebutSemaine));
        for (long semaine = 0; ; semaine += regle.Intervalle)
        {
            var debutDeSemaine = origine.AddDays(semaine * 7);
            foreach (var decalage in decalages)
                yield return debutDeSemaine.AddDays(decalage) + heure;
            if (debutDeSemaine > borne)
                yield break;
        }
    }

    private static int DecalageDansSemaine(DayOfWeek jour, DayOfWeek debutSemaine)
        => (((int)jour - (int)debutSemaine) + 7) % 7;

    private static IEnumerable<DateTime> GenererMensuelles(RegleRecurrence regle, DateTime debut, DateTime borne)
    {
        var heure = debut.TimeOfDay;
        var moisOrigine = new DateTime(debut.Year, debut.Month, 1);
        for (long k = 0; ; k += regle.Intervalle)
        {
            var mois = moisOrigine.AddMonths(checked((int)k));
            foreach (var jour in JoursDansMois(regle, mois.Year, mois.Month, debut.Day))
                yield return new DateTime(mois.Year, mois.Month, jour) + heure;
            if (mois > borne) // borne franchie même si aucun jour valide (ex. « le 31 » en cascade de mois courts)
                yield break;
        }
    }

    private static IEnumerable<int> JoursDansMois(RegleRecurrence regle, int annee, int mois, int jourParDefaut)
    {
        var joursDuMois = DateTime.DaysInMonth(annee, mois);
        var resultat = new SortedSet<int>();

        if (regle.JoursDuMois.Count > 0)
        {
            foreach (var v in regle.JoursDuMois)
            {
                var jour = v > 0 ? v : joursDuMois + v + 1;
                if (jour >= 1 && jour <= joursDuMois) // jour invalide → sauté (RFC 5545)
                    resultat.Add(jour);
            }
        }
        else if (regle.JoursSemaine.Count > 0)
        {
            foreach (var (ordinal, jourSemaine) in regle.JoursSemaine)
            {
                if (ordinal == 0)
                {
                    // Tous les jours de ce nom dans le mois.
                    for (var j = 1; j <= joursDuMois; j++)
                        if (new DateTime(annee, mois, j).DayOfWeek == jourSemaine)
                            resultat.Add(j);
                }
                else if (NiemeJourDuMois(annee, mois, ordinal, jourSemaine) is { } jour)
                {
                    resultat.Add(jour);
                }
                // n-ième inexistant (ex. 5e lundi) → mois sauté pour cette valeur (RFC 5545)
            }
        }
        else if (jourParDefaut <= joursDuMois)
        {
            // Jour de DTSTART ; « le 31 » saute les mois trop courts (RFC 5545).
            resultat.Add(jourParDefaut);
        }

        return resultat;
    }

    private static int? NiemeJourDuMois(int annee, int mois, int ordinal, DayOfWeek jourSemaine)
    {
        var joursDuMois = DateTime.DaysInMonth(annee, mois);
        if (ordinal > 0)
        {
            var premier = new DateTime(annee, mois, 1);
            var premierBon = 1 + ((((int)jourSemaine - (int)premier.DayOfWeek) + 7) % 7);
            var jour = premierBon + (ordinal - 1) * 7;
            return jour <= joursDuMois ? jour : null;
        }
        else
        {
            var dernier = new DateTime(annee, mois, joursDuMois);
            var dernierBon = joursDuMois - ((((int)dernier.DayOfWeek - (int)jourSemaine) + 7) % 7);
            var jour = dernierBon + (ordinal + 1) * 7;
            return jour >= 1 ? jour : null;
        }
    }

    private static IEnumerable<DateTime> GenererAnnuelles(RegleRecurrence regle, DateTime debut, DateTime borne)
    {
        var heure = debut.TimeOfDay;
        var moisCibles = (regle.MoisDeLAnnee.Count > 0 ? regle.MoisDeLAnnee : [debut.Month])
            .Distinct().OrderBy(m => m).ToArray();

        for (long k = 0; ; k += regle.Intervalle)
        {
            var annee = checked(debut.Year + (int)k);
            if (annee > borne.Year + 1) // borne franchie même sans jour valide (ex. 29 février)
                yield break;
            foreach (var mois in moisCibles)
            {
                IEnumerable<int> jours;
                if (regle.JoursDuMois.Count > 0)
                {
                    var joursDuMois = DateTime.DaysInMonth(annee, mois);
                    jours = regle.JoursDuMois
                        .Select(v => v > 0 ? v : joursDuMois + v + 1)
                        .Where(j => j >= 1 && j <= joursDuMois)
                        .Distinct().OrderBy(j => j);
                }
                else
                {
                    // Jour de DTSTART ; « 29 février » n'existe que les années bissextiles (RFC 5545).
                    jours = debut.Day <= DateTime.DaysInMonth(annee, mois) ? [debut.Day] : [];
                }

                foreach (var jour in jours)
                    yield return new DateTime(annee, mois, jour) + heure;
            }
        }
    }
}
