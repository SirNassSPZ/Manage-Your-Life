using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Projection;
using Xunit;

namespace DeuxiemeCerveau.Core.Tests;

// ----- Jeux de référence (§12.2) : entrées connues → projection attendue -----

public sealed record GoldenScenario(string Nom, string Description, GoldenRequete Requete, List<GoldenMois> Attendu);

public sealed record GoldenRequete(
    string PremierMois, int NombreMois, GoldenSolde SoldeReference, List<Element> Elements);

public sealed record GoldenSolde(long SoldeReferenceCentimes, DateOnly SoldeReferenceDate);

public sealed record GoldenMois(
    string Mois, long? OuvertureCentimes, long EntreesCentimes, long SortiesCentimes,
    long? ClotureCentimes, bool Decouvert, bool AvantReference);

public class GoldenProjectionTests
{
    private static string DossierGolden => Path.Combine(AppContext.BaseDirectory, "golden");

    public static TheoryData<string> Fichiers()
    {
        var donnees = new TheoryData<string>();
        foreach (var fichier in Directory.EnumerateFiles(DossierGolden, "*.json").Order())
            donnees.Add(Path.GetFileName(fichier));
        return donnees;
    }

    [Fact]
    public void Les_sept_scenarios_sont_presents()
        => Assert.Equal(7, Directory.EnumerateFiles(DossierGolden, "*.json").Count());

    [Theory]
    [MemberData(nameof(Fichiers))]
    public void Golden_file_projection_conforme(string fichier)
    {
        var scenario = SerialisationCanonique.Deserialiser<GoldenScenario>(
            File.ReadAllText(Path.Combine(DossierGolden, fichier)));

        var requete = new RequeteProjection(
            MoisCalendaire.Analyser(scenario.Requete.PremierMois),
            scenario.Requete.NombreMois,
            new SoldeReference(
                scenario.Requete.SoldeReference.SoldeReferenceCentimes,
                scenario.Requete.SoldeReference.SoldeReferenceDate),
            scenario.Requete.Elements);

        var resultat = CalculateurProjection.Calculer(requete);

        Assert.Equal(scenario.Attendu.Count, resultat.Count);
        for (var i = 0; i < resultat.Count; i++)
        {
            var attendu = scenario.Attendu[i];
            var obtenu = resultat[i];
            var mois = $"{obtenu.Annee:D4}-{obtenu.Mois:D2}";
            Assert.True(attendu.Mois == mois,
                $"{scenario.Nom}[{i}] : mois {mois}, attendu {attendu.Mois}");
            Assert.True(attendu.OuvertureCentimes == obtenu.OuvertureCentimes,
                $"{scenario.Nom}[{mois}] : ouverture {obtenu.OuvertureCentimes}, attendu {attendu.OuvertureCentimes}");
            Assert.True(attendu.EntreesCentimes == obtenu.EntreesCentimes,
                $"{scenario.Nom}[{mois}] : entrées {obtenu.EntreesCentimes}, attendu {attendu.EntreesCentimes}");
            Assert.True(attendu.SortiesCentimes == obtenu.SortiesCentimes,
                $"{scenario.Nom}[{mois}] : sorties {obtenu.SortiesCentimes}, attendu {attendu.SortiesCentimes}");
            Assert.True(attendu.ClotureCentimes == obtenu.ClotureCentimes,
                $"{scenario.Nom}[{mois}] : clôture {obtenu.ClotureCentimes}, attendu {attendu.ClotureCentimes}");
            Assert.True(attendu.Decouvert == obtenu.Decouvert,
                $"{scenario.Nom}[{mois}] : découvert {obtenu.Decouvert}, attendu {attendu.Decouvert}");
            Assert.True(attendu.AvantReference == obtenu.AvantReference,
                $"{scenario.Nom}[{mois}] : avant_reference {obtenu.AvantReference}, attendu {attendu.AvantReference}");
        }
    }
}

// ----- Cas unitaires complémentaires -----

public class CalculateurProjectionTests
{
    private static RequeteProjection Requete(int nombreMois = 2, long solde = 100000,
        DateOnly? date = null, params Element[] elements)
        => new(new MoisCalendaire(2026, 7), nombreMois,
            new SoldeReference(solde, date ?? new DateOnly(2026, 7, 1)), elements);

    [Fact]
    public void Sans_elements_le_solde_reste_plat()
    {
        var resultat = CalculateurProjection.Calculer(Requete());
        Assert.All(resultat, m =>
        {
            Assert.Equal(100000, m.OuvertureCentimes);
            Assert.Equal(100000, m.ClotureCentimes);
            Assert.False(m.Decouvert);
        });
    }

    [Fact]
    public void Nombre_de_mois_borne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculateurProjection.Calculer(Requete(nombreMois: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalculateurProjection.Calculer(Requete(nombreMois: CalculateurProjection.NombreMoisMax + 1)));
    }

    [Fact]
    public void Element_financier_sans_date_ou_sans_montant_ignore()
    {
        var sansDate = Fabrique.Facture(fuseau: null);
        sansDate.DateDebut = null;
        var sansMontant = Fabrique.Facture();
        sansMontant.MontantCentimes = null;

        var resultat = CalculateurProjection.Calculer(Requete(elements: [sansDate, sansMontant]));
        Assert.Equal(100000, resultat[0].ClotureCentimes);
    }

    [Fact]
    public void Elements_non_financiers_ignores()
    {
        var tache = Fabrique.Tache();
        var resultat = CalculateurProjection.Calculer(Requete(elements: [tache]));
        Assert.Equal(0, resultat[0].SortiesCentimes);
    }

    [Fact]
    public void Rattachement_au_mois_local_pas_au_mois_utc()
    {
        // Kiritimati (UTC+14) : le 1er août 00:30 local = 31 juillet 10:30 UTC.
        // L'occurrence appartient au mois d'AOÛT (ce que voit l'utilisateur), pas à juillet (D-004).
        var facture = Fabrique.Facture(
            dateDebut: new DateTimeOffset(2026, 7, 31, 10, 30, 0, TimeSpan.Zero),
            fuseau: "Pacific/Kiritimati", montant: 5000);

        var resultat = CalculateurProjection.Calculer(Requete(elements: [facture]));
        Assert.Equal(0, resultat[0].SortiesCentimes);    // juillet : rien
        Assert.Equal(5000, resultat[1].SortiesCentimes); // août : la facture
    }

    [Fact]
    public void Occurrence_apres_la_reference_mais_mois_local_anterieur_rattachee_au_premier_mois()
    {
        // Pago Pago (UTC−11) : le 31 juillet 23:00 local = 1er août 10:00 UTC — postérieure à
        // l'instant de référence (1er août 00:00Z) mais de mois local juillet → rattachée à août (D-004).
        var facture = Fabrique.Facture(
            dateDebut: new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            fuseau: "Pacific/Pago_Pago", montant: 7000);

        var resultat = CalculateurProjection.Calculer(
            Requete(date: new DateOnly(2026, 8, 1), nombreMois: 3, elements: [facture]));

        Assert.True(resultat[0].AvantReference);          // juillet : avant la référence
        Assert.Equal(7000, resultat[1].SortiesCentimes);  // août : la facture repliée
        Assert.Equal(93000, resultat[1].ClotureCentimes);
    }

    [Fact]
    public void Occurrence_le_jour_meme_de_la_reference_incluse()
    {
        // « Aujourd'hui j'ai X » : le solde du matin ne contient pas encore les occurrences du jour (D-004).
        var facture = Fabrique.Facture(
            dateDebut: new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), montant: 1000);
        var resultat = CalculateurProjection.Calculer(Requete(elements: [facture]));
        Assert.Equal(1000, resultat[0].SortiesCentimes);
    }

    [Fact]
    public void Sens_derive_du_type_quand_absent()
    {
        var facture = Fabrique.Facture(montant: 1000);
        facture.Sens = null; // donnée historique dégradée : le type fait foi
        var resultat = CalculateurProjection.Calculer(Requete(elements: [facture]));
        Assert.Equal(1000, resultat[0].SortiesCentimes);
    }
}

public class MoisCalendaireTests
{
    [Fact]
    public void Arithmetique_a_travers_les_annees()
    {
        Assert.Equal(new MoisCalendaire(2027, 1), new MoisCalendaire(2026, 12).AjouterMois(1));
        Assert.Equal(new MoisCalendaire(2026, 12), new MoisCalendaire(2027, 1).AjouterMois(-1));
        Assert.Equal(new MoisCalendaire(2027, 6), new MoisCalendaire(2026, 7).AjouterMois(11));
        Assert.Equal(new MoisCalendaire(2025, 7), new MoisCalendaire(2026, 7).AjouterMois(-12));
    }

    [Fact]
    public void Analyse_et_format()
    {
        Assert.Equal(new MoisCalendaire(2026, 7), MoisCalendaire.Analyser("2026-07"));
        Assert.Equal("2026-07", new MoisCalendaire(2026, 7).ToString());
        Assert.Throws<FormatException>(() => MoisCalendaire.Analyser("2026/07"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoisCalendaire(2026, 13));
    }
}
