using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;
using Xunit;

namespace DeuxiemeCerveau.Api.Tests;

/// <summary>
/// Tests de contrat de l'API (§8) sur la couche service — le critère « fini » de l'Étape 3 en local :
/// push idempotent (deux fois le même lot = même état), pull par curseur, projection conforme.
/// Les mêmes scénarios seront rejoués en HTTP contre l'instance dev déployée (incrément 3d).
/// </summary>
public class ServiceApiTests
{
    private readonly ServiceApi _service;

    public ServiceApiTests()
    {
        var horloge = new HorlogeFixe(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        _service = new ServiceApi(new MagasinSynchroMemoire(), new MagasinAppareilsMemoire(horloge), horloge);
    }

    [Fact]
    public void Enregistrement_appareil_renvoie_un_id()
    {
        var reponse = _service.EnregistrerAppareil(new DemandeEnregistrementAppareil("PC de test", "windows"));
        Assert.NotEqual(Guid.Empty, reponse.AppareilId);
    }

    [Fact]
    public void Push_puis_pull_rend_l_entite_visible()
    {
        var facture = FabriqueApi.Facture();
        var push = _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(facture, EntiteSynchro.Element)));
        Assert.Equal(ResultatChangement.Applique, push.Resultats[0].Resultat);

        var page = _service.Tirer(depuis: 0, limite: 100);
        var entite = Assert.Single(page.Entites);
        Assert.Equal(facture.Id, entite.Id);
        Assert.Equal("Loyer", entite.Payload.GetProperty("titre").GetString());
        Assert.Equal(page.Curseur, push.Resultats[0].ServerSeq);
    }

    [Fact]
    public void Push_du_meme_lot_deux_fois_donne_le_meme_etat()
    {
        var lot = FabriqueApi.Lot(FabriqueApi.Changement(FabriqueApi.Facture(), EntiteSynchro.Element));

        var premier = _service.Pousser(lot);
        var etat1 = _service.Tirer(0, 100);

        var second = _service.Pousser(lot); // coupure réseau simulée : le lot repart tel quel
        var etat2 = _service.Tirer(0, 100);

        Assert.False(premier.Resultats[0].Rejoue);
        Assert.True(second.Resultats[0].Rejoue);
        Assert.Equal(premier.Resultats[0].ServerSeq, second.Resultats[0].ServerSeq);
        Assert.Equal(etat1.Curseur, etat2.Curseur);                 // aucune séquence consommée
        Assert.Single(etat2.Entites);                               // pas de doublon
    }

    [Fact]
    public void Pull_par_curseur_pagine_et_reprend()
    {
        for (var i = 0; i < 5; i++)
            _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(
                FabriqueApi.Facture(titre: $"F{i}"), EntiteSynchro.Element)));

        var page1 = _service.Tirer(0, limite: 2);
        Assert.Equal(2, page1.Entites.Count);
        Assert.True(page1.Encore);

        var page2 = _service.Tirer(page1.Curseur, limite: 2);
        var page3 = _service.Tirer(page2.Curseur, limite: 2);
        Assert.Single(page3.Entites);
        Assert.False(page3.Encore);
    }

    [Fact]
    public void Lot_invalide_rejete_en_entier()
    {
        var valide = FabriqueApi.Facture();
        var invalide = FabriqueApi.Facture();
        invalide.Statut = StatutElement.Fait; // interdit pour une facture (§3.1)

        Assert.Throws<ErreurLotInvalide>(() => _service.Pousser(FabriqueApi.Lot(
            FabriqueApi.Changement(valide, EntiteSynchro.Element),
            FabriqueApi.Changement(invalide, EntiteSynchro.Element))));

        Assert.Empty(_service.Tirer(0, 100).Entites); // rien appliqué (atomicité §6.2.2)
    }

    // ----- Projection (§5.1) -----

    [Fact]
    public void Projection_sans_solde_de_reference_refusee()
        => Assert.Throws<SoldeReferenceAbsent>(() => _service.Projeter(12));

    [Fact]
    public void Projection_conforme_apres_recalage_et_saisie()
    {
        // Solde de référence : 150 000 centimes au 1er juillet 2026.
        _service.Recaler(new DemandeRecalageSolde(
            Guid.NewGuid(), 150000, new DateOnly(2026, 7, 1), FabriqueApi.T0, FabriqueApi.AppareilA));

        // Un loyer mensuel de 800 € à partir du 5 juillet.
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(
            FabriqueApi.Facture(recurrence: "FREQ=MONTHLY"), EntiteSynchro.Element)));

        var projection = _service.Projeter(3);
        // Le mois courant (fixé au 15 juillet) est le premier de l'horizon.
        Assert.Equal("2026-07", projection.Mois[0].Mois);
        Assert.Equal(150000, projection.Mois[0].OuvertureCentimes);
        Assert.Equal(80000, projection.Mois[0].SortiesCentimes);   // loyer de juillet
        Assert.Equal(70000, projection.Mois[0].ClotureCentimes);
        Assert.Equal(-10000, projection.Mois[1].ClotureCentimes);  // août : 70000 - 80000
        Assert.True(projection.Mois[1].Decouvert);
    }

    // ----- Purge (§5.6, D-010) -----

    [Fact]
    public void Purge_depuis_la_corbeille_detruit_et_propage()
    {
        var facture = FabriqueApi.Facture();
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(facture, EntiteSynchro.Element)));

        // Mise à la corbeille.
        var supprimee = FabriqueApi.Copier(facture);
        supprimee.Supprime = true;
        supprimee.DateSuppression = FabriqueApi.T0.AddHours(1);
        supprimee.DateModification = FabriqueApi.T0.AddHours(1);
        supprimee.Version = 2;
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(supprimee, EntiteSynchro.Element)));
        var curseurAvant = _service.Tirer(0, 100).Curseur;

        var reponse = _service.Purger(new LotPurge
        {
            AppareilId = FabriqueApi.AppareilA,
            Purges = [new DemandePurge { ChangeId = Guid.NewGuid(), Entite = EntiteSynchro.Element, EntiteId = facture.Id }],
        });
        Assert.Equal(StatutPurge.Purgee, reponse.Resultats[0].Statut);

        // Le pull suivant transporte la purge (pierre tombale), l'entité n'est plus dans les états.
        var page = _service.Tirer(curseurAvant, 100);
        Assert.Empty(page.Entites);
        Assert.Contains(page.Purges, p => p.Id == facture.Id);
    }
}
