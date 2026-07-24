using DeuxiemeCerveau.Api.Persistence;
using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Moteur de synchro (§6.2) — Étape 4d. Scénarios de parité solo (§12) joués contre le VRAI serveur
/// (ServiceApi) en process : saisie hors-ligne puis reconnexion, coupure réseau en plein push sans
/// doublon (idempotence), convergence par pull, arbitrage dernière-écriture-gagne, purge propagée.
/// </summary>
public sealed class MoteurSynchroTests : IDisposable
{
    private readonly HorlogeFixe _horloge = new(FabriqueLocale.T0);
    private readonly ServiceApi _service;
    private readonly FauxClientApi _api;
    private readonly List<BaseLocale> _bases = [];

    public MoteurSynchroTests()
    {
        _service = new ServiceApi(
            new MagasinSynchroMemoire(), new MagasinAppareilsMemoire(_horloge), _horloge, new StockagePiecesMemoire());
        _api = new FauxClientApi(_service);
    }

    public void Dispose()
    {
        foreach (var b in _bases)
            b.Dispose();
    }

    private (BaseLocale b, ServiceSaisie saisie, MoteurSynchro moteur) Appareil()
    {
        var b = FabriqueLocale.BaseMemoire();
        _bases.Add(b);
        var id = new IdentiteAppareil(b.Depot);
        return (b, new ServiceSaisie(b.Depot, id, _horloge), new MoteurSynchro(b.Depot, id, _api));
    }

    private Element Element(BaseLocale b, Guid id)
        => SerialisationCanonique.Deserialiser<Element>(b.Depot.Obtenir(EntiteSynchro.Element, id)!.PayloadCanonique);

    [Fact]
    public async Task Saisie_hors_ligne_puis_reconnexion_televerse_tout()
    {
        var (b, saisie, moteur) = Appareil();
        var f1 = FabriqueLocale.NouvelleFacture(titre: "loyer");
        var f2 = FabriqueLocale.NouvelleFacture(titre: "internet");
        saisie.Enregistrer(f1, EntiteSynchro.Element); // hors-ligne : rien qu'en local + outbox
        saisie.Enregistrer(f2, EntiteSynchro.Element);
        Assert.Equal(2, b.Depot.Outbox().Count);

        await moteur.Synchroniser("PC test", "windows"); // reconnexion

        Assert.Empty(b.Depot.Outbox()); // tout a été envoyé

        // Une seconde installation qui synchronise voit les deux saisies.
        var (b2, _, moteur2) = Appareil();
        await moteur2.Synchroniser("PC 2", "windows");
        Assert.Equal(2, b2.Depot.Enumerer(EntiteSynchro.Element).Count);
    }

    [Fact]
    public async Task Coupure_en_plein_push_ne_cree_pas_de_doublon()
    {
        var (b, saisie, moteur) = Appareil();
        var facture = FabriqueLocale.NouvelleFacture();
        saisie.Enregistrer(facture, EntiteSynchro.Element);

        _api.CoupuresAvantReponsePush = 1; // le serveur traite le lot, mais la réponse se perd
        await Assert.ThrowsAsync<HttpRequestException>(() => moteur.Pousser());
        Assert.Single(b.Depot.Outbox()); // rien retiré : la confirmation n'est pas arrivée

        await moteur.Pousser(); // renvoi du même lot : le serveur déduplique par change_id (§6.2.1)
        Assert.Empty(b.Depot.Outbox());

        // Le serveur ne détient qu'UNE entité — aucun doublon malgré le renvoi.
        var (b2, _, moteur2) = Appareil();
        await moteur2.Synchroniser("PC 2", "windows");
        Assert.Single(b2.Depot.Enumerer(EntiteSynchro.Element));
    }

    [Fact]
    public async Task Deux_appareils_convergent_par_pull()
    {
        var (ba, sa, ma) = Appareil();
        var facture = FabriqueLocale.NouvelleFacture(titre: "v-A");
        sa.Enregistrer(facture, EntiteSynchro.Element);
        await ma.Synchroniser("A", "windows");

        var (bb, sb, mb) = Appareil();
        await mb.Synchroniser("B", "windows");
        Assert.NotNull(bb.Depot.Obtenir(EntiteSynchro.Element, facture.Id)); // B a reçu la saisie de A

        var vueB = Element(bb, facture.Id);
        vueB.Titre = "v-B";
        sb.Enregistrer(vueB, EntiteSynchro.Element);
        await mb.Synchroniser("B", "windows");

        await ma.Synchroniser("A", "windows"); // A tire la modif de B
        Assert.Equal("v-B", Element(ba, facture.Id).Titre);
    }

    [Fact]
    public async Task Conflit_derniere_ecriture_gagne_et_les_deux_convergent()
    {
        var (ba, sa, ma) = Appareil();
        var facture = FabriqueLocale.NouvelleFacture(titre: "origine");
        sa.Enregistrer(facture, EntiteSynchro.Element);
        await ma.Synchroniser("A", "windows");
        var (bb, sb, mb) = Appareil();
        await mb.Synchroniser("B", "windows");

        // A et B modifient la même version concurremment, à des instants distincts.
        var vueA = Element(ba, facture.Id); vueA.Titre = "gagnant-A";
        var vueB = Element(bb, facture.Id); vueB.Titre = "perdant-B";
        _horloge.MaintenantUtc = FabriqueLocale.T0.AddMinutes(20); sa.Enregistrer(vueA, EntiteSynchro.Element); // plus récent
        _horloge.MaintenantUtc = FabriqueLocale.T0.AddMinutes(10); sb.Enregistrer(vueB, EntiteSynchro.Element); // plus ancien

        await mb.Synchroniser("B", "windows"); // B pousse d'abord
        await ma.Synchroniser("A", "windows"); // A pousse ensuite ; plus récent → gagne l'arbitrage
        await mb.Synchroniser("B", "windows"); // B tire le gagnant

        Assert.Equal("gagnant-A", Element(ba, facture.Id).Titre);
        Assert.Equal("gagnant-A", Element(bb, facture.Id).Titre); // les deux convergent (filet 3 : le perdant est au journal)
    }

    [Fact]
    public async Task Une_purge_recue_detruit_la_copie_locale()
    {
        var (_, sa, ma) = Appareil();
        var facture = FabriqueLocale.NouvelleFacture(titre: "à purger");
        sa.Enregistrer(facture, EntiteSynchro.Element);
        sa.Supprimer(EntiteSynchro.Element, facture.Id); // à la corbeille (supprime = true)
        await ma.Synchroniser("A", "windows");

        var (bb, _, mb) = Appareil();
        await mb.Synchroniser("B", "windows");
        Assert.NotNull(bb.Depot.Obtenir(EntiteSynchro.Element, facture.Id)); // B a la version corbeille

        // Purge définitive côté serveur (comme si A la purgeait depuis la corbeille, §5.6).
        var reponse = await _api.Purger(new LotPurge
        {
            AppareilId = Guid.NewGuid(),
            Purges = [new DemandePurge { ChangeId = Guid.NewGuid(), Entite = EntiteSynchro.Element, EntiteId = facture.Id }],
        });
        Assert.Equal(StatutPurge.Purgee, reponse.Resultats[0].Statut);

        await mb.Synchroniser("B", "windows"); // B tire : la purge est transportée
        Assert.Null(bb.Depot.Obtenir(EntiteSynchro.Element, facture.Id)); // la copie locale de B a disparu
    }
}
