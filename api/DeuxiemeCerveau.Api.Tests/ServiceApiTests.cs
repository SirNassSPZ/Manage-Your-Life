using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Api.Persistence;
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
        _service = new ServiceApi(
            new MagasinSynchroMemoire(), new MagasinAppareilsMemoire(horloge), horloge, new StockagePiecesMemoire());
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

    [Fact]
    public void La_projection_porte_le_solde_courant()
    {
        // Le chiffre que l'accueil affiche en grand (§5.1). Il voyage AVEC la projection : c'est le
        // même état lu au même instant, et deux routes séparées pourraient se contredire.
        // L'horloge de test est fixée au 15 juillet 2026, le loyer tombe le 5 : il est passé.
        _service.Recaler(new DemandeRecalageSolde(
            Guid.NewGuid(), 150000, new DateOnly(2026, 7, 1), FabriqueApi.T0, FabriqueApi.AppareilA));

        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(
            FabriqueApi.Facture(recurrence: "FREQ=MONTHLY"), EntiteSynchro.Element)));

        Assert.Equal(70000, _service.Projeter(3).SoldeCourantCentimes); // 150 000 − 80 000
    }

    [Fact]
    public void Une_envie_ne_deplace_ni_la_projection_ni_le_solde_courant()
    {
        // GARDE-FOU, à travers l'API cette fois (D-027 : « deux tests le défendent explicitement,
        // un dans le cœur et un à travers l'API — ne pas les supprimer en croyant à des doublons »).
        // Depuis que l'envie peut porter un prix, ce n'est plus l'absence du champ qui protège le
        // calcul mais son exclusion stricte. C'est le « ne rien inventer sur les envies d'achat ».
        _service.Recaler(new DemandeRecalageSolde(
            Guid.NewGuid(), 150000, new DateOnly(2026, 7, 1), FabriqueApi.T0, FabriqueApi.AppareilA));

        var envie = FabriqueApi.Facture();
        envie.Type = TypeElement.Envie;
        envie.Sens = null;              // interdit sur une envie (§3.1)
        envie.DateDebut = null;
        envie.Fuseau = null;
        envie.MontantCentimes = 30000;  // prix prêté, facultatif (D-027)
        envie.Statut = StatutElement.Idee;

        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(envie, EntiteSynchro.Element)));

        var projection = _service.Projeter(3);
        Assert.Equal(150000, projection.SoldeCourantCentimes);
        Assert.Equal(150000, projection.Mois[0].ClotureCentimes);
        Assert.Equal(0, projection.Mois[0].SortiesCentimes);
    }

    // ----- Confrontation d'une envie au budget (§5.1bis, D-027) -----

    /// <summary>Solde de 1 500 € au 1er juillet 2026, sans aucun mouvement : la cascade est plate.</summary>
    private void PoserSolde(long centimes = 150000) => _service.Recaler(new DemandeRecalageSolde(
        Guid.NewGuid(), centimes, new DateOnly(2026, 7, 1), FabriqueApi.T0, FabriqueApi.AppareilA));

    [Fact]
    public void Confrontation_sans_solde_de_reference_refusee()
        => Assert.Throws<SoldeReferenceAbsent>(() =>
            _service.Confronter(10000, new MoisCalendaire(2026, 8), 12));

    [Fact]
    public void Confrontation_qui_passe_rend_les_deux_cascades()
    {
        PoserSolde();

        var reponse = _service.Confronter(30000, new MoisCalendaire(2026, 9), 6);

        Assert.True(reponse.Passe);
        Assert.Null(reponse.PremierMoisQuiCasse);
        Assert.Equal(0, reponse.ManqueCentimes);
        Assert.Equal("2026-09", reponse.MoisCible);

        // Les deux cascades sont rendues pour que l'app montre l'écart, pas un simple oui/non.
        Assert.Equal(150000, reponse.Nominale[2].ClotureCentimes);
        Assert.Equal(120000, reponse.Simulee[2].ClotureCentimes);
    }

    [Fact]
    public void Confrontation_qui_ne_passe_pas_nomme_le_mois_et_le_manque()
    {
        PoserSolde();

        var reponse = _service.Confronter(200000, new MoisCalendaire(2026, 8), 6);

        Assert.False(reponse.Passe);
        Assert.Equal("2026-08", reponse.PremierMoisQuiCasse);
        Assert.Equal(50000, reponse.ManqueCentimes);   // 1 500 € − 2 000 € = −500 €
    }

    [Fact]
    public void Confrontation_hors_horizon_refusee()
    {
        PoserSolde();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Confronter(10000, new MoisCalendaire(2030, 1), 6));
    }

    [Fact]
    public void Une_confrontation_n_ecrit_rien()
    {
        // Règle 9 : c'est une lecture. Ni Élément, ni occurrence, ni entrée au journal — et la
        // projection nominale doit être exactement la même avant et après.
        PoserSolde();
        var avant = _service.Projeter(6);

        _service.Confronter(200000, new MoisCalendaire(2026, 8), 6);
        _service.Confronter(999999, new MoisCalendaire(2026, 12), 6);

        var apres = _service.Projeter(6);
        Assert.Equal(avant.Mois.Select(m => m.ClotureCentimes), apres.Mois.Select(m => m.ClotureCentimes));
        Assert.All(apres.Mois, m => Assert.Equal(0, m.SortiesCentimes));
    }

    [Fact]
    public void Une_envie_avec_un_prix_ne_pese_pas_sur_la_projection()
    {
        // LE garde-fou de D-027, vérifié de bout en bout à travers l'API : le prix d'une envie
        // n'entre jamais dans /projection/budget. Seule la confrontation le fait apparaître.
        PoserSolde();
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(
            FabriqueApi.Envie(montantCentimes: 18000), EntiteSynchro.Element)));

        var projection = _service.Projeter(6);

        Assert.All(projection.Mois, m => Assert.Equal(0, m.SortiesCentimes));
        Assert.Equal(150000, projection.Mois[5].ClotureCentimes);
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

    // ----- Pièces jointes (§7, §8) -----

    [Fact]
    public void Piece_jointe_url_envoi_puis_confirmation()
    {
        var elementId = Guid.NewGuid();
        var envoi = _service.PreparerEnvoiPiece(elementId, tailleOctets: 2048, pieceId: null);

        Assert.NotEqual(Guid.Empty, envoi.AttachmentId);
        Assert.Contains(envoi.AttachmentId.ToString(), envoi.BlobPath); // chemin dérivé des identifiants
        Assert.False(string.IsNullOrWhiteSpace(envoi.UploadUrl));

        // Le binaire « arrive » dans le stockage (simulé) → la confirmation réussit et renvoie la taille.
        var confirmation = _service.ConfirmerEnvoiPiece(envoi.BlobPath);
        Assert.True(confirmation.Confirme);
        Assert.True(confirmation.TailleOctets > 0);
    }

    [Fact]
    public void Piece_jointe_trop_volumineuse_refusee()
        => Assert.Throws<PieceTropVolumineuse>(() =>
            _service.PreparerEnvoiPiece(Guid.NewGuid(), PieceJointe.TailleMaxOctets + 1, null));

    [Fact]
    public void Confirmation_sans_televersement_refusee()
        => Assert.Throws<TeleversementAbsent>(() => _service.ConfirmerEnvoiPiece("element/piece-jamais-envoyee"));

    [Fact]
    public void Url_lecture_apres_synchro_des_metadonnees()
    {
        // Les métadonnées d'une pièce transitent par la synchro (§6.2), comme toute entité.
        var elementId = Guid.NewGuid();
        var envoi = _service.PreparerEnvoiPiece(elementId, 2048, null);
        var piece = new PieceJointe
        {
            Id = envoi.AttachmentId, ElementId = elementId, NomFichier = "facture.pdf",
            TailleOctets = 2048, BlobPath = envoi.BlobPath, Confirme = true,
            DateCreation = FabriqueApi.T0, DateModification = FabriqueApi.T0,
            AppareilSource = FabriqueApi.AppareilA, Version = 1,
        };
        _service.Pousser(FabriqueApi.Lot(FabriqueApi.Changement(piece, EntiteSynchro.PieceJointe)));

        var lecture = _service.UrlLecturePiece(envoi.AttachmentId);
        Assert.Equal("facture.pdf", lecture.NomFichier);
        Assert.Contains(envoi.BlobPath, lecture.DownloadUrl);
    }

    [Fact]
    public void Url_lecture_piece_inconnue_refusee()
        => Assert.Throws<PieceIntrouvable>(() => _service.UrlLecturePiece(Guid.NewGuid()));
}
