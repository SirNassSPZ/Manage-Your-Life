using DeuxiemeCerveau.Api.Persistence;
using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Projection budgétaire (§5.1) — Étape 4e. Le calcul reste SERVEUR (§4) : l'app pousse ses données
/// puis lit la projection via l'API. On vérifie que le résultat reflète les données synchronisées.
/// </summary>
public sealed class ProjectionTests : IDisposable
{
    private readonly HorlogeFixe _horloge = new(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly ServiceApi _service;
    private readonly FauxClientApi _api;
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();

    public ProjectionTests()
    {
        _service = new ServiceApi(
            new MagasinSynchroMemoire(), new MagasinAppareilsMemoire(_horloge), _horloge, new StockagePiecesMemoire());
        _api = new FauxClientApi(_service);
    }

    public void Dispose() => _base.Dispose();

    [Fact]
    public async Task La_projection_reflete_les_donnees_synchronisees()
    {
        var id = new IdentiteAppareil(_base.Depot);
        var saisie = new ServiceSaisie(_base.Depot, id, _horloge);
        var moteur = new MoteurSynchro(_base.Depot, id, _api);

        saisie.Enregistrer(FabriqueLocale.Reglage(150000, new DateOnly(2026, 7, 1)), EntiteSynchro.Reglage);
        saisie.Enregistrer(
            FabriqueLocale.NouvelleFacture(titre: "loyer", montant: 80000, recurrence: "FREQ=MONTHLY"),
            EntiteSynchro.Element);
        await moteur.Synchroniser("A", "windows"); // la projection est serveur : il faut d'abord pousser

        var projection = await _api.Projeter(3);

        Assert.Equal("2026-07", projection.Mois[0].Mois); // mois courant (horloge au 15 juillet)
        Assert.Equal(150000, projection.Mois[0].OuvertureCentimes);
        Assert.Equal(80000, projection.Mois[0].SortiesCentimes);
        Assert.Equal(70000, projection.Mois[0].ClotureCentimes);
        Assert.Equal(-10000, projection.Mois[1].ClotureCentimes); // août : 70000 - 80000
        Assert.True(projection.Mois[1].Decouvert);
    }
}
