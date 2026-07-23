using DeuxiemeCerveau.Core.Recurrence;
using DeuxiemeCerveau.Core.Temps;
using Xunit;

namespace DeuxiemeCerveau.Core.Tests;

/// <summary>Analyse des RRULE : sous-ensemble supporté, rejet bruyant du reste (D-003).</summary>
public class RegleRecurrenceTests
{
    [Theory]
    [InlineData("FREQ=DAILY")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,FR")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=5")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1")]
    [InlineData("FREQ=MONTHLY;BYDAY=2MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,7;BYMONTHDAY=15")]
    [InlineData("FREQ=DAILY;COUNT=10")]
    [InlineData("FREQ=DAILY;UNTIL=20261231")]
    [InlineData("FREQ=DAILY;UNTIL=20261231T235959Z")]
    [InlineData("RRULE:FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=-1")]
    [InlineData("FREQ=WEEKLY;WKST=SU;BYDAY=SA,SU")]
    public void Sous_ensemble_supporte_accepte(string rrule)
        => RegleRecurrence.Analyser(rrule);

    [Theory]
    [InlineData("")]
    [InlineData("FREQ=HOURLY")]                       // fréquences infra-journalières exclues
    [InlineData("FREQ=SECONDLY")]
    [InlineData("INTERVAL=2")]                        // FREQ obligatoire
    [InlineData("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=FR")] // BYSETPOS exclu
    [InlineData("FREQ=YEARLY;BYWEEKNO=20")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=100")]
    [InlineData("FREQ=DAILY;BYHOUR=9")]
    [InlineData("FREQ=DAILY;COUNT=5;UNTIL=20261231")] // mutuellement exclusifs (RFC)
    [InlineData("FREQ=DAILY;COUNT=0")]
    [InlineData("FREQ=DAILY;INTERVAL=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=32")]
    [InlineData("FREQ=WEEKLY;BYDAY=2MO")]             // ordinal interdit en WEEKLY
    [InlineData("FREQ=YEARLY;BYDAY=MO")]              // BYDAY interdit en YEARLY
    [InlineData("FREQ=DAILY;BYDAY=MO")]               // BYDAY interdit en DAILY
    [InlineData("FREQ=WEEKLY;BYMONTHDAY=5")]          // BYMONTHDAY interdit en WEEKLY
    [InlineData("FREQ=MONTHLY;BYMONTH=3")]            // BYMONTH réservé à YEARLY
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=5;BYDAY=MO")]
    [InlineData("FREQ=DAILY;UNTIL=20261231T235959")]  // UNTIL date-heure sans Z
    [InlineData("FREQ=DAILY;FANTAISIE=1")]
    [InlineData("FREQ=DAILY;FREQ=WEEKLY")]            // partie dupliquée
    [InlineData("FREQ=MONTHLY;BYDAY=6MO")]            // ordinal hors ±1..5
    public void Hors_sous_ensemble_rejete_bruyamment(string rrule)
        => Assert.Throws<ErreurRecurrence>(() => RegleRecurrence.Analyser(rrule));
}

/// <summary>
/// Expansion des RRULE dans le fuseau de l'Élément (règle 6) — les cas tordus exigés par l'Étape 1 :
/// dernier jour du mois, tous les 2 mois, changements d'heure, mois trop courts.
/// </summary>
public class ExpanseurRecurrenceTests
{
    private static readonly TimeZoneInfo Paris = FuseauxIana.Exiger("Europe/Paris");
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static List<Occurrence> Expanser(string rrule, DateTimeOffset debutUtc, TimeZoneInfo tz,
        DateTimeOffset utcMax)
        => ExpanseurRecurrence.Expanser(RegleRecurrence.Analyser(rrule), debutUtc, tz, utcMax).ToList();

    private static DateTimeOffset Utc0(int a, int m, int j, int h = 0, int min = 0)
        => new(a, m, j, h, min, 0, TimeSpan.Zero);

    [Fact]
    public void Dtstart_est_la_premiere_occurrence()
    {
        var occurrences = Expanser("FREQ=DAILY", Utc0(2026, 7, 1, 10), Utc, Utc0(2026, 7, 3, 23));
        Assert.Equal([Utc0(2026, 7, 1, 10), Utc0(2026, 7, 2, 10), Utc0(2026, 7, 3, 10)],
            occurrences.Select(o => o.Utc));
    }

    [Fact]
    public void Count_compte_dtstart_pour_un()
    {
        var occurrences = Expanser("FREQ=DAILY;COUNT=5", Utc0(2026, 7, 1, 10), Utc, Utc0(2027, 1, 1));
        Assert.Equal(5, occurrences.Count);
        Assert.Equal(Utc0(2026, 7, 5, 10), occurrences[^1].Utc);
    }

    [Fact]
    public void Until_date_heure_borne_incluse()
    {
        var occurrences = Expanser("FREQ=DAILY;UNTIL=20260704T100000Z", Utc0(2026, 7, 1, 10), Utc,
            Utc0(2027, 1, 1));
        Assert.Equal(4, occurrences.Count); // 1, 2, 3 et 4 juillet — la borne est incluse
    }

    [Fact]
    public void Until_forme_date_comparee_en_date_locale()
    {
        // DTSTART : 1er juillet 00:30 heure de Paris (30 juin 22:30Z). L'occurrence du 4 juillet
        // 00:30 locale tombe le 3 juillet en UTC (22:30Z) : une comparaison sur la date UTC
        // l'inclurait à tort — la forme date d'UNTIL se compare sur la date LOCALE (D-003).
        var occurrences = Expanser("FREQ=DAILY;UNTIL=20260703",
            new DateTimeOffset(2026, 6, 30, 22, 30, 0, TimeSpan.Zero), Paris, Utc0(2027, 1, 1));
        Assert.Equal(3, occurrences.Count); // 1er, 2 et 3 juillet en heure locale
        Assert.All(occurrences, o => Assert.True(DateOnly.FromDateTime(o.Locale) <= new DateOnly(2026, 7, 3)));
    }

    [Fact]
    public void Mensuel_le_31_saute_les_mois_courts()
    {
        // §12 : « récurrences tordues ». RFC 5545 : les dates invalides sont ignorées.
        var occurrences = Expanser("FREQ=MONTHLY",
            new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero), Paris, Utc0(2026, 12, 31, 23));
        Assert.Equal([1, 3, 5, 7, 8, 10, 12], occurrences.Select(o => o.Locale.Month));
        Assert.All(occurrences, o => Assert.Equal(31, o.Locale.Day));
    }

    [Fact]
    public void Dernier_jour_du_mois()
    {
        var occurrences = Expanser("FREQ=MONTHLY;BYMONTHDAY=-1",
            new DateTimeOffset(2026, 1, 31, 16, 0, 0, TimeSpan.Zero), Paris, Utc0(2026, 6, 30, 23));
        Assert.Equal([(1, 31), (2, 28), (3, 31), (4, 30), (5, 31), (6, 30)],
            occurrences.Select(o => (o.Locale.Month, o.Locale.Day)));
    }

    [Fact]
    public void Dernier_jour_du_mois_tous_les_2_mois()
    {
        var occurrences = Expanser("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=-1",
            new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero), Paris, Utc0(2026, 12, 31, 23));
        Assert.Equal([(1, 31), (3, 31), (5, 31), (7, 31), (9, 30), (11, 30)],
            occurrences.Select(o => (o.Locale.Month, o.Locale.Day)));
    }

    [Fact]
    public void Annuel_29_fevrier_annees_bissextiles_seulement()
    {
        var occurrences = Expanser("FREQ=YEARLY",
            new DateTimeOffset(2024, 2, 29, 12, 0, 0, TimeSpan.Zero), Utc, Utc0(2029, 12, 31));
        Assert.Equal([2024, 2028], occurrences.Select(o => o.Locale.Year));
    }

    [Fact]
    public void Mensuel_dernier_vendredi()
    {
        var occurrences = Expanser("FREQ=MONTHLY;BYDAY=-1FR",
            new DateTimeOffset(2026, 7, 31, 6, 0, 0, TimeSpan.Zero), Paris, Utc0(2026, 9, 30, 23));
        Assert.Equal([(7, 31), (8, 28), (9, 25)],
            occurrences.Select(o => (o.Locale.Month, o.Locale.Day)));
        Assert.All(occurrences, o => Assert.Equal(DayOfWeek.Friday, o.Locale.DayOfWeek));
    }

    [Fact]
    public void Mensuel_deuxieme_lundi()
    {
        var occurrences = Expanser("FREQ=MONTHLY;BYDAY=2MO",
            new DateTimeOffset(2026, 7, 13, 7, 0, 0, TimeSpan.Zero), Paris, Utc0(2026, 9, 30, 23));
        Assert.Equal([(7, 13), (8, 10), (9, 14)],
            occurrences.Select(o => (o.Locale.Month, o.Locale.Day)));
    }

    [Fact]
    public void Hebdomadaire_intervalle_2_multi_jours()
    {
        // DTSTART mercredi 1er juillet 2026, 10:00 Paris — BYDAY=MO,FR, une semaine sur deux.
        var occurrences = Expanser("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,FR",
            new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), Paris, Utc0(2026, 7, 31, 23));
        Assert.Equal([(7, 1), (7, 3), (7, 13), (7, 17), (7, 27), (7, 31)],
            occurrences.Select(o => (o.Locale.Month, o.Locale.Day)));
    }

    [Fact]
    public void Hebdomadaire_par_defaut_jour_de_dtstart()
    {
        var occurrences = Expanser("FREQ=WEEKLY",
            new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), Paris, Utc0(2026, 7, 31, 23));
        Assert.Equal([1, 8, 15, 22, 29], occurrences.Select(o => o.Locale.Day));
        Assert.All(occurrences, o => Assert.Equal(DayOfWeek.Wednesday, o.Locale.DayOfWeek));
    }

    // ----- Changements d'heure (Europe/Paris 2026 : 29 mars 02:00→03:00, 25 octobre 03:00→02:00) -----

    [Fact]
    public void Passage_heure_ete_heure_inexistante_decalee_pas_sautee()
    {
        // Quotidien à 02:30 locale : le 29 mars 2026, 02:30 n'existe pas → 03:30 (saut d'une heure).
        var occurrences = Expanser("FREQ=DAILY",
            new DateTimeOffset(2026, 3, 28, 1, 30, 0, TimeSpan.Zero), Paris, Utc0(2026, 3, 30, 23));

        Assert.Equal(3, occurrences.Count); // 28, 29, 30 mars — aucune occurrence perdue
        Assert.Equal(new DateTime(2026, 3, 28, 2, 30, 0), occurrences[0].Locale);
        Assert.Equal(Utc0(2026, 3, 28, 1, 30), occurrences[0].Utc);       // CET (+1)
        Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), occurrences[1].Locale); // décalée
        Assert.Equal(Utc0(2026, 3, 29, 1, 30), occurrences[1].Utc);       // CEST (+2)
        Assert.Equal(new DateTime(2026, 3, 30, 2, 30, 0), occurrences[2].Locale);
        Assert.Equal(Utc0(2026, 3, 30, 0, 30), occurrences[2].Utc);
    }

    [Fact]
    public void Retour_heure_hiver_heure_ambigue_premiere_occurrence()
    {
        // Quotidien à 02:30 locale : le 25 octobre 2026, 02:30 existe deux fois → première (CEST, +2).
        var occurrences = Expanser("FREQ=DAILY",
            new DateTimeOffset(2026, 10, 24, 0, 30, 0, TimeSpan.Zero), Paris, Utc0(2026, 10, 26, 23));

        Assert.Equal(3, occurrences.Count);
        Assert.Equal(Utc0(2026, 10, 24, 0, 30), occurrences[0].Utc); // CEST (+2)
        Assert.Equal(Utc0(2026, 10, 25, 0, 30), occurrences[1].Utc); // ambiguë → CEST (+2)
        Assert.Equal(Utc0(2026, 10, 26, 1, 30), occurrences[2].Utc); // CET (+1)
    }

    [Fact]
    public void Le_loyer_du_5_reste_le_5_a_travers_les_changements_heure()
    {
        // Règle 6, mot pour mot : « un loyer “le 5” reste le 5, changements d'heure compris ».
        var occurrences = Expanser("FREQ=MONTHLY",
            new DateTimeOffset(2026, 2, 5, 8, 0, 0, TimeSpan.Zero), Paris, Utc0(2026, 11, 30, 23));

        Assert.All(occurrences, o =>
        {
            Assert.Equal(5, o.Locale.Day);
            Assert.Equal(9, o.Locale.Hour); // 09:00 locale, toute l'année
        });
        // Offset UTC : +1 en hiver (08:00Z), +2 en été (07:00Z).
        Assert.Equal(Utc0(2026, 2, 5, 8), occurrences[0].Utc);  // février : CET
        Assert.Equal(Utc0(2026, 4, 5, 7), occurrences[2].Utc);  // avril : CEST
        Assert.Equal(Utc0(2026, 11, 5, 8), occurrences[9].Utc); // novembre : CET
    }

    [Fact]
    public void Regle_sans_aucune_occurrence_valide_ne_boucle_pas()
    {
        // « Le 31 » tous les 12 mois ancré en février de... jamais : la borne arrête proprement.
        var occurrences = Expanser("FREQ=MONTHLY;INTERVAL=12;BYMONTHDAY=31",
            new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero), Utc, Utc0(2030, 1, 1));
        Assert.Single(occurrences); // uniquement DTSTART (le 10 février, première occurrence par contrat)
    }

    [Fact]
    public void Fenetre_utc_max_respectee()
    {
        var occurrences = Expanser("FREQ=DAILY", Utc0(2026, 7, 1, 10), Utc, Utc0(2026, 7, 2, 9, 59));
        Assert.Single(occurrences);
    }
}

/// <summary>Fuseaux IANA : validation stricte et conventions de conversion (D-002).</summary>
public class FuseauxTests
{
    [Theory]
    [InlineData("Europe/Paris")]
    [InlineData("America/New_York")]
    [InlineData("Pacific/Kiritimati")]
    [InlineData("UTC")]
    public void Identifiants_iana_acceptes(string id) => Assert.True(FuseauxIana.EstValide(id));

    [Theory]
    [InlineData("Romance Standard Time")] // identifiant Windows — rejeté (D-002)
    [InlineData("Pacific Standard Time")]
    [InlineData("Europe/Nulle_Part")]
    [InlineData("")]
    [InlineData(null)]
    public void Identifiants_non_iana_rejetes(string? id) => Assert.False(FuseauxIana.EstValide(id));

    [Fact]
    public void Conversion_simple_vers_utc()
    {
        var paris = FuseauxIana.Exiger("Europe/Paris");
        var utc = ConvertisseurFuseau.VersUtc(new DateTime(2026, 7, 5, 9, 0, 0), paris);
        Assert.Equal(new DateTimeOffset(2026, 7, 5, 7, 0, 0, TimeSpan.Zero), utc); // CEST +2
    }
}
