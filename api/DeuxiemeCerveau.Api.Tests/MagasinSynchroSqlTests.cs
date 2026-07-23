using System.Data.Common;
using DeuxiemeCerveau.Api.Persistence;
using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Migrations;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DeuxiemeCerveau.Api.Tests;

/// <summary>
/// Le magasin SQL exécuté contre une vraie base SQLite, montée par les vraies migrations (§9, D-012).
/// Prouve que l'adaptateur SQL respecte le contrat d'<see cref="IMagasinSynchro"/> — même comportement
/// que le magasin mémoire de référence. Le même code tourne sur Azure SQL en production.
/// </summary>
public sealed class MagasinSynchroSqlTests : IDisposable
{
    private readonly SqliteConnection _connexion;
    private readonly MagasinSynchroSql _magasin;
    private readonly ServiceApi _service;

    public MagasinSynchroSqlTests()
    {
        // SQLite en mémoire : la connexion doit rester ouverte pour que la base survive.
        _connexion = new SqliteConnection("Data Source=:memory:");
        _connexion.Open();

        var appliquees = new CibleMigrationSql(_connexion, DialecteSql.Sqlite).AppliquerAuDemarrage();
        Assert.Equal(ListeMigrations.Toutes.Count, appliquees.Count);

        DbConnection Fabrique() => _connexion; // connexion partagée (base :memory:)
        _magasin = new MagasinSynchroSql(Fabrique, DialecteSql.Sqlite);
        var horloge = new HorlogeFixe(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        _service = new ServiceApi(_magasin, new MagasinAppareilsMemoire(horloge), horloge);
    }

    public void Dispose() => _connexion.Dispose();

    [Fact]
    public void Push_puis_pull_aller_retour_fidele()
    {
        var facture = FabriqueApi.Facture();
        facture.Description = "avec accents : éàü";
        facture.Categories.Add(Guid.NewGuid());
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(facture, EntiteSynchro.Element)));

        var etat = _magasin.Obtenir(EntiteSynchro.Element, facture.Id);
        Assert.NotNull(etat);
        var relu = SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);
        Assert.Equal(facture.Titre, relu.Titre);
        Assert.Equal("avec accents : éàü", relu.Description);
        Assert.Equal(facture.MontantCentimes, relu.MontantCentimes);
        Assert.Single(relu.Categories);
        Assert.Equal(1, etat.Version);
        Assert.Equal(1, etat.ServerSeq);
    }

    [Fact]
    public void Idempotence_le_meme_lot_deux_fois()
    {
        var lot = FabriqueApi.Lot(FabriqueApi.Changement(FabriqueApi.Facture(), EntiteSynchro.Element));
        var premier = _service.Pousser(lot);
        var second = _service.Pousser(lot);

        Assert.False(premier.Resultats[0].Rejoue);
        Assert.True(second.Resultats[0].Rejoue);
        Assert.Equal(premier.Resultats[0].ServerSeq, second.Resultats[0].ServerSeq);
        Assert.Single(_service.Tirer(0, 100).Entites);
        Assert.Equal(1, _magasin.SeqCourante);
    }

    [Fact]
    public void Conflit_derniere_ecriture_gagne_perdant_au_journal()
    {
        var origine = FabriqueApi.Facture();
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(origine, EntiteSynchro.Element)));

        var deB = FabriqueApi.Copier(origine);
        deB.Titre = "Version récente";
        deB.Version = 2;
        deB.DateModification = origine.DateModification.AddMinutes(20);
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(deB, EntiteSynchro.Element)));

        var deA = FabriqueApi.Copier(origine);
        deA.Titre = "Version ancienne";
        deA.Version = 2;
        deA.DateModification = origine.DateModification.AddMinutes(10); // plus ancienne → perd
        var reponse = _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(deA, EntiteSynchro.Element)));

        Assert.Equal(ResultatChangement.PerdantArchive, reponse.Resultats[0].Resultat);
        var etat = _magasin.Obtenir(EntiteSynchro.Element, origine.Id)!;
        Assert.Equal("Version récente", SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique).Titre);
    }

    [Fact]
    public void Atomicite_lot_invalide_ne_laisse_aucune_trace()
    {
        var valide = FabriqueApi.Facture();
        var invalide = FabriqueApi.Facture();
        invalide.Statut = StatutElement.Fait; // interdit (§3.1)

        Assert.Throws<ErreurLotInvalide>(() => _service.Pousser(FabriqueApi.Lot(
            FabriqueApi.Changement(valide, EntiteSynchro.Element),
            FabriqueApi.Changement(invalide, EntiteSynchro.Element))));

        Assert.Null(_magasin.Obtenir(EntiteSynchro.Element, valide.Id)); // rollback réel
        Assert.Equal(0, _magasin.SeqCourante);
    }

    [Fact]
    public void Reglage_solde_persiste_et_projection_calcule()
    {
        _service.Recaler(new DemandeRecalageSolde(
            Guid.NewGuid(), 150000, new DateOnly(2026, 7, 1), FabriqueApi.T0, FabriqueApi.AppareilA));
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(
            FabriqueApi.Facture(recurrence: "FREQ=MONTHLY"), EntiteSynchro.Element)));

        var projection = _service.Projeter(2);
        Assert.Equal(150000, projection.Mois[0].OuvertureCentimes);
        Assert.Equal(70000, projection.Mois[0].ClotureCentimes);
        Assert.Equal(-10000, projection.Mois[1].ClotureCentimes);
    }

    [Fact]
    public void Purge_detruit_et_pose_la_tombale()
    {
        var facture = FabriqueApi.Facture();
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(facture, EntiteSynchro.Element)));
        var supprimee = FabriqueApi.Copier(facture);
        supprimee.Supprime = true;
        supprimee.DateSuppression = FabriqueApi.T0.AddHours(1);
        supprimee.DateModification = FabriqueApi.T0.AddHours(1);
        supprimee.Version = 2;
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(supprimee, EntiteSynchro.Element)));
        var curseur = _service.Tirer(0, 100).Curseur;

        _service.Purger(new LotPurge
        {
            AppareilId = FabriqueApi.AppareilA,
            Purges = [new DemandePurge { ChangeId = Guid.NewGuid(), Entite = EntiteSynchro.Element, EntiteId = facture.Id }],
        });

        Assert.Null(_magasin.Obtenir(EntiteSynchro.Element, facture.Id));
        Assert.NotNull(_magasin.ObtenirTombale(EntiteSynchro.Element, facture.Id));
        var page = _service.Tirer(curseur, 100);
        Assert.Contains(page.Purges, p => p.Id == facture.Id);
    }

    [Fact]
    public void Pull_multi_entites_ordonne_par_server_seq()
    {
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(FabriqueApi.Facture(titre: "A"), EntiteSynchro.Element)));
        var categorie = new Categorie
        {
            Id = Guid.NewGuid(), Nom = "santé", Couleur = "#00AA55", Origine = OrigineCategorie.Transversale,
            DateCreation = FabriqueApi.T0, DateModification = FabriqueApi.T0, AppareilSource = FabriqueApi.AppareilA, Version = 1,
        };
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(categorie, EntiteSynchro.Categorie)));

        var page = _service.Tirer(0, 100);
        Assert.Equal(2, page.Entites.Count);
        Assert.Equal(EntiteSynchro.Element, page.Entites[0].Entite);
        Assert.Equal(EntiteSynchro.Categorie, page.Entites[1].Entite);
        Assert.Equal(2, page.Curseur);
    }

    [Fact]
    public void Cascade_fermeture_projet_reporte_les_taches()
    {
        var projet = new Projet
        {
            Id = Guid.NewGuid(), Nom = "MMA", Couleur = "#3366FF", Statut = StatutProjet.Actif,
            DateCreation = FabriqueApi.T0, DateModification = FabriqueApi.T0, AppareilSource = FabriqueApi.AppareilA, Version = 1,
        };
        var tache = new Element
        {
            Id = Guid.NewGuid(), Type = TypeElement.Tache, Titre = "Réviser", Statut = StatutElement.AFaire,
            ProjetId = projet.Id, DateCreation = FabriqueApi.T0, DateModification = FabriqueApi.T0,
            AppareilSource = FabriqueApi.AppareilA, Version = 1,
        };
        _service.Pousser(FabriqueApi.Lot(
            FabriqueApi.Changement(projet, EntiteSynchro.Projet),
            FabriqueApi.Changement(tache, EntiteSynchro.Element)));

        var fermeture = FabriqueApi.Copier2(projet);
        fermeture.Statut = StatutProjet.Termine;
        fermeture.Version = 2;
        fermeture.DateModification = FabriqueApi.T0.AddHours(1);
        var reponse = _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(fermeture, EntiteSynchro.Projet)));

        Assert.Single(reponse.ChangementsInduits);
        var etatTache = _magasin.Obtenir(EntiteSynchro.Element, tache.Id)!;
        Assert.Equal(StatutElement.Reporte,
            SerialisationCanonique.Deserialiser<Element>(etatTache.PayloadCanonique).Statut);
    }
}
