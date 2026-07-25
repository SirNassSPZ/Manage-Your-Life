using DeuxiemeCerveau.Api.Persistence;
using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Vue « Budget projeté » (feuille de route Palier 1 rang 5 / recos #5, #6) — Étape 4f.
/// La projection reste SERVEUR (§4) : on pousse les données, on lit la projection, et on vérifie que
/// l'app la restitue et désigne le mois rouge AVEC ses plus grosses sorties (le levier d'action).
/// </summary>
public sealed class BudgetTests : IDisposable
{
    private readonly HorlogeFixe _horloge = new(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();
    private readonly FauxClientApi _api;
    private readonly ServiceSaisie _saisie;
    private readonly MoteurSynchro _moteur;
    private readonly ServiceBudget _budget;

    public BudgetTests()
    {
        var service = new ServiceApi(
            new MagasinSynchroMemoire(), new MagasinAppareilsMemoire(_horloge), _horloge, new StockagePiecesMemoire());
        _api = new FauxClientApi(service);
        var id = new IdentiteAppareil(_base.Depot);
        _saisie = new ServiceSaisie(_base.Depot, id, _horloge);
        _moteur = new MoteurSynchro(_base.Depot, id, _api);
        _budget = new ServiceBudget(_api, new ServiceCalendrier(_base.Depot));
    }

    public void Dispose() => _base.Dispose();

    [Fact]
    public async Task L_horizon_est_restitue_et_le_mois_rouge_est_designe()
    {
        _saisie.Enregistrer(FabriqueLocale.Reglage(150000, new DateOnly(2026, 7, 1)), EntiteSynchro.Reglage);
        _saisie.Enregistrer(
            FabriqueLocale.NouvelleFacture(titre: "loyer", montant: 80000, recurrence: "FREQ=MONTHLY"),
            EntiteSynchro.Element);
        await _moteur.Synchroniser("A", "windows"); // la projection est serveur : pousser d'abord

        var vue = await _budget.Charger(mois: 3);

        Assert.Equal(3, vue.Mois.Count);
        Assert.Equal("2026-07", vue.Mois[0].Mois);
        Assert.Equal(70000, vue.Mois[0].ClotureCentimes);   // 150000 − 80000
        Assert.False(vue.Mois[0].Decouvert);

        Assert.NotNull(vue.ProchaineAlerte);                 // août passe dans le rouge (70000 − 80000)
        Assert.Equal("2026-08", vue.ProchaineAlerte!.Mois);
        Assert.Equal(-10000, vue.ProchaineAlerte.ClotureCentimes);
    }

    [Fact]
    public async Task L_alerte_montre_les_plus_grosses_sorties_du_mois_rouge()
    {
        _saisie.Enregistrer(FabriqueLocale.Reglage(150000, new DateOnly(2026, 7, 1)), EntiteSynchro.Reglage);
        _saisie.Enregistrer(
            FabriqueLocale.NouvelleFacture(titre: "loyer", montant: 80000, recurrence: "FREQ=MONTHLY"),
            EntiteSynchro.Element);
        var petite = FabriqueLocale.NouvelleFacture(titre: "internet", montant: 3000, recurrence: "FREQ=MONTHLY");
        _saisie.Enregistrer(petite, EntiteSynchro.Element);
        await _moteur.Synchroniser("A", "windows");

        var vue = await _budget.Charger(mois: 3, sortiesEnVedette: 2);

        Assert.NotNull(vue.ProchaineAlerte);
        var alerte = vue.ProchaineAlerte!;
        Assert.Equal(2, alerte.PlusGrossesSorties.Count);
        Assert.Equal("loyer", alerte.PlusGrossesSorties[0].Titre);      // la plus grosse d'abord
        Assert.Equal(80000, alerte.PlusGrossesSorties[0].MontantCentimes);
        Assert.Equal("internet", alerte.PlusGrossesSorties[1].Titre);
        // Les occurrences citées sont bien celles du mois rouge (août), pas d'un autre mois.
        Assert.All(alerte.PlusGrossesSorties, o => Assert.Equal(8, o.InstantUtc.Month));
    }

    [Fact]
    public async Task Sans_mois_rouge_il_n_y_a_pas_d_alerte()
    {
        _saisie.Enregistrer(FabriqueLocale.Reglage(500000, new DateOnly(2026, 7, 1)), EntiteSynchro.Reglage);
        _saisie.Enregistrer(
            FabriqueLocale.NouvelleFacture(titre: "loyer", montant: 10000, recurrence: "FREQ=MONTHLY"),
            EntiteSynchro.Element);
        await _moteur.Synchroniser("A", "windows");

        var vue = await _budget.Charger(mois: 3);

        Assert.Null(vue.ProchaineAlerte);
        Assert.All(vue.Mois, m => Assert.False(m.Decouvert));
    }

    [Fact]
    public void Un_mois_mal_forme_ne_fait_pas_planter_la_vue()
    {
        Assert.Empty(_budget.SortiesDuMois("pas-un-mois"));
        Assert.Empty(_budget.SortiesDuMois("2026-07", combien: 0));
    }
}
