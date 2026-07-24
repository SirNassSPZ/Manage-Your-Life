using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Accueil « Aujourd'hui » (feuille de route Palier 1, rang 1 / reco #10) — Étape 4f.
/// On vérifie l'assemblage LOCAL (solde de référence + occurrences datées), sans jamais recalculer la
/// projection (§4 : le budget projeté reste serveur).
/// </summary>
public sealed class AujourdhuiTests : IDisposable
{
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();
    private readonly ServiceSaisie _saisie;
    private readonly ServiceAujourdhui _accueil;

    public AujourdhuiTests()
    {
        var id = new IdentiteAppareil(_base.Depot);
        _saisie = new ServiceSaisie(_base.Depot, id, new HorlogeFixe(FabriqueLocale.T0));
        _accueil = new ServiceAujourdhui(_base.Depot, new ServiceCalendrier(_base.Depot));
    }

    public void Dispose() => _base.Dispose();

    private static Element FactureLe(DateTimeOffset date, string titre, string? rrule = null)
        => new()
        {
            Type = TypeElement.Facture, Titre = titre, DateDebut = date, Fuseau = "Europe/Paris",
            Recurrence = rrule, MontantCentimes = 5000, Devise = "EUR",
            Sens = Sens.Sortie, Statut = StatutElement.AVenir,
        };

    // Heure locale de l'appareil (Paris, UTC+2 en été) — comme le passerait la coquille WinUI.
    private static DateTimeOffset ParisLe(int jour, int heure = 12)
        => new(2026, 7, jour, heure, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Solde_de_reference_absent_puis_present()
    {
        Assert.Null(_accueil.SoldeDeReference()); // jamais posé → l'accueil invitera à le faire (reco #1)

        _saisie.Enregistrer(FabriqueLocale.Reglage(200000, new DateOnly(2026, 7, 3)), EntiteSynchro.Reglage);

        var solde = _accueil.SoldeDeReference();
        Assert.NotNull(solde);
        Assert.Equal(200000, solde!.Centimes);
        Assert.Equal(new DateOnly(2026, 7, 3), solde.Date);
    }

    [Fact]
    public void Aujourdhui_ne_montre_que_les_occurrences_du_jour()
    {
        _saisie.Enregistrer(FactureLe(new DateTimeOffset(2026, 7, 5, 7, 0, 0, TimeSpan.Zero), "loyer", "FREQ=MONTHLY"), EntiteSynchro.Element);
        _saisie.Enregistrer(FactureLe(new DateTimeOffset(2026, 7, 9, 7, 0, 0, TimeSpan.Zero), "internet"), EntiteSynchro.Element);

        var duJour = _accueil.Aujourdhui(ParisLe(5));

        Assert.Single(duJour);
        Assert.Equal("loyer", duJour[0].Titre); // le 9 (internet) n'est pas « aujourd'hui »
    }

    [Fact]
    public void Prochains_jours_groupe_par_jour_et_respecte_la_fenetre()
    {
        _saisie.Enregistrer(FactureLe(new DateTimeOffset(2026, 7, 5, 7, 0, 0, TimeSpan.Zero), "loyer"), EntiteSynchro.Element);
        _saisie.Enregistrer(FactureLe(new DateTimeOffset(2026, 7, 5, 16, 0, 0, TimeSpan.Zero), "courses"), EntiteSynchro.Element);
        _saisie.Enregistrer(FactureLe(new DateTimeOffset(2026, 7, 9, 7, 0, 0, TimeSpan.Zero), "internet"), EntiteSynchro.Element);
        _saisie.Enregistrer(FactureLe(new DateTimeOffset(2026, 7, 20, 7, 0, 0, TimeSpan.Zero), "assurance"), EntiteSynchro.Element);

        var agenda = _accueil.ProchainsJours(ParisLe(5), jours: 7); // fenêtre 5 → 11 juillet

        Assert.Equal(2, agenda.Count);                              // le 5 et le 9 ; le 20 est hors fenêtre
        Assert.Equal(new DateOnly(2026, 7, 5), agenda[0].Jour);
        Assert.Equal(2, agenda[0].Occurrences.Count);              // loyer + courses regroupés le même jour
        Assert.Equal(new DateOnly(2026, 7, 9), agenda[1].Jour);
        Assert.Single(agenda[1].Occurrences);
        Assert.DoesNotContain(agenda.SelectMany(j => j.Occurrences), o => o.Titre == "assurance");
    }
}
