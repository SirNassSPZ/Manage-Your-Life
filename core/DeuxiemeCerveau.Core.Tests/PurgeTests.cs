using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;
using Xunit;

namespace DeuxiemeCerveau.Core.Tests;

/// <summary>
/// La purge (§5.6, D-010, spec v3.2) : seule destruction réelle de l'application — arbitrée par
/// le serveur, propagée par le pull, protégée par pierre tombale. La conservation gagne toute course.
/// </summary>
public class ProcesseurPurgeTests
{
    private readonly MagasinSynchroMemoire _magasin = new();
    private readonly ProcesseurPush _push;
    private readonly ProcesseurPurge _purge;
    private readonly ProcesseurPull _pull;

    public ProcesseurPurgeTests()
    {
        var horloge = new HorlogeFixe(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
        _push = new ProcesseurPush(_magasin, horloge);
        _purge = new ProcesseurPurge(_magasin, horloge);
        _pull = new ProcesseurPull(_magasin);
    }

    /// <summary>Crée puis met à la corbeille un Élément — le point de départ de toute purge légitime.</summary>
    private Element CreerPuisSupprimer(Element? element = null)
    {
        var facture = element ?? Fabrique.Facture();
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(facture, EntiteSynchro.Element)));

        var supprimee = Fabrique.Copier(facture);
        supprimee.Supprime = true;
        supprimee.DateSuppression = facture.DateModification.AddHours(1);
        supprimee.DateModification = facture.DateModification.AddHours(1);
        supprimee.Version = facture.Version + 1;
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(supprimee, EntiteSynchro.Element)));
        return supprimee;
    }

    private static LotPurge Lot(params DemandePurge[] purges)
        => new() { AppareilId = Fabrique.AppareilA, Purges = [.. purges] };

    private static DemandePurge Demande(Guid entiteId, EntiteSynchro entite = EntiteSynchro.Element,
        Guid? changeId = null)
        => new() { ChangeId = changeId ?? Guid.NewGuid(), Entite = entite, EntiteId = entiteId };

    // ----- Purge nominale : destruction réelle, journal caviardé, tombale, pull -----

    [Fact]
    public void Purge_depuis_la_corbeille_detruit_reellement()
    {
        var element = CreerPuisSupprimer();
        var curseurAvant = _pull.Traiter(0).Curseur;

        var reponse = _purge.Traiter(Lot(Demande(element.Id)));

        var resultat = Assert.Single(reponse.Resultats);
        Assert.Equal(StatutPurge.Purgee, resultat.Statut);

        // L'état est détruit — la seule destruction réelle de l'application (§5.6).
        Assert.Null(_magasin.Obtenir(EntiteSynchro.Element, element.Id));

        // Le journal est caviardé : plus aucun payload, mais métadonnées et séquences intactes (D-010.2).
        var entrees = _magasin.Journal.Where(j => j.EntiteId == element.Id).ToList();
        Assert.Equal(3, entrees.Count); // création, suppression, purge
        Assert.All(entrees, j => Assert.Equal(ProcesseurPurge.MarqueurPurge, j.Payload));
        Assert.Equal(ResultatChangement.Purge, entrees[^1].Resultat);

        // La pierre tombale est posée et le pull la transporte (propagation, D-010.2).
        Assert.NotNull(_magasin.ObtenirTombale(EntiteSynchro.Element, element.Id));
        var page = _pull.Traiter(curseurAvant);
        Assert.Empty(page.Entites);
        var tombale = Assert.Single(page.Purges);
        Assert.Equal(element.Id, tombale.Id);
        Assert.True(page.Curseur > curseurAvant);
    }

    [Fact]
    public void Purge_rejouee_meme_issue_sans_double_effet()
    {
        var element = CreerPuisSupprimer();
        var lot = Lot(Demande(element.Id));

        var premiere = _purge.Traiter(lot);
        var journalApres = _magasin.Journal.Count;
        var seconde = _purge.Traiter(lot); // coupure réseau : le lot repart tel quel

        var resultat = Assert.Single(seconde.Resultats);
        Assert.True(resultat.Rejoue);
        Assert.Equal(StatutPurge.Purgee, resultat.Statut);
        Assert.Equal(premiere.Resultats[0].ServerSeq, resultat.ServerSeq);
        Assert.Equal(journalApres, _magasin.Journal.Count);
    }

    [Fact]
    public void Purges_concurrentes_depuis_deux_appareils_une_seule_tombale()
    {
        var element = CreerPuisSupprimer();
        _purge.Traiter(Lot(Demande(element.Id)));

        // B purge la même entité avec son propre change_id : succès de fait, pas de double tombale.
        var deB = new LotPurge { AppareilId = Fabrique.AppareilB, Purges = [Demande(element.Id)] };
        var reponse = _purge.Traiter(deB);

        Assert.Equal(StatutPurge.Purgee, reponse.Resultats[0].Statut);
        Assert.False(reponse.Resultats[0].Rejoue);
        Assert.Equal(Fabrique.AppareilA,
            _magasin.ObtenirTombale(EntiteSynchro.Element, element.Id)!.AppareilId); // la première fait foi
    }

    // ----- La conservation gagne toute course (D-010.1) -----

    [Fact]
    public void Purge_refusee_si_l_entite_a_ete_restauree()
    {
        var element = CreerPuisSupprimer();

        // Restauration (autre appareil) arrivée avant la purge.
        var restauree = Fabrique.Copier(element);
        restauree.Supprime = false;
        restauree.DateSuppression = null;
        restauree.Version = element.Version + 1;
        restauree.DateModification = element.DateModification.AddHours(1);
        restauree.AppareilSource = Fabrique.AppareilB;
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilB, Fabrique.Changement(restauree, EntiteSynchro.Element)));

        var reponse = _purge.Traiter(Lot(Demande(element.Id)));

        var resultat = Assert.Single(reponse.Resultats);
        Assert.Equal(StatutPurge.Refusee, resultat.Statut);
        Assert.Equal(ProcesseurPurge.MotifRestauree, resultat.Motif);
        Assert.NotNull(_magasin.Obtenir(EntiteSynchro.Element, element.Id)); // rien n'est détruit
        Assert.Null(_magasin.ObtenirTombale(EntiteSynchro.Element, element.Id));

        // Le refus est journalisé : le rejeu de la même demande redonne « refusée », même si
        // l'entité repasse à la corbeille entre-temps (l'issue d'un change_id est définitive).
        var rejeu = _purge.Traiter(Lot(Demande(element.Id, changeId: reponse.Resultats[0].ChangeId)));
        Assert.True(rejeu.Resultats[0].Rejoue);
        Assert.Equal(StatutPurge.Refusee, rejeu.Resultats[0].Statut);
    }

    [Fact]
    public void Purge_d_une_entite_encore_active_refusee()
    {
        var facture = Fabrique.Facture();
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(facture, EntiteSynchro.Element)));

        var reponse = _purge.Traiter(Lot(Demande(facture.Id)));
        Assert.Equal(StatutPurge.Refusee, reponse.Resultats[0].Statut);
        Assert.Equal(ProcesseurPurge.MotifRestauree, reponse.Resultats[0].Motif);
    }

    [Fact]
    public void Purge_d_une_entite_inconnue_refusee()
    {
        var reponse = _purge.Traiter(Lot(Demande(Guid.NewGuid())));
        Assert.Equal(StatutPurge.Refusee, reponse.Resultats[0].Statut);
        Assert.Equal(ProcesseurPurge.MotifInconnue, reponse.Resultats[0].Motif); // jamais de destruction à l'aveugle
    }

    [Fact]
    public void Le_reglage_n_est_pas_purgeable()
        => Assert.Throws<ErreurLotInvalide>(() => _purge.Traiter(
            Lot(Demande(ReglageSolde.IdSoldeReference, EntiteSynchro.Reglage))));

    // ----- Anti-résurrection (D-010.3) -----

    [Fact]
    public void Changement_retardataire_vers_une_entite_purgee_refuse_sans_archivage()
    {
        var element = CreerPuisSupprimer();
        _purge.Traiter(Lot(Demande(element.Id)));

        // Un appareil resté hors-ligne pousse une vieille édition de l'entité purgée.
        var retardataire = Fabrique.Copier(element);
        retardataire.Titre = "Contenu ressuscité";
        retardataire.Supprime = false;
        retardataire.DateSuppression = null;
        retardataire.Version = element.Version + 5;
        retardataire.DateModification = element.DateModification.AddDays(1);
        retardataire.AppareilSource = Fabrique.AppareilB;
        var changement = Fabrique.Changement(retardataire, EntiteSynchro.Element);
        var reponse = _push.Traiter(Fabrique.Lot(Fabrique.AppareilB, changement));

        var resultat = Assert.Single(reponse.Resultats);
        Assert.Equal(ResultatChangement.RefusePurge, resultat.Resultat);

        // Ni résurrection de l'état, ni archivage du contenu (entorse assumée au filet 3, D-010.3).
        Assert.Null(_magasin.Obtenir(EntiteSynchro.Element, element.Id));
        Assert.DoesNotContain(_magasin.Journal, j => j.Payload.Contains("Contenu ressuscité"));

        // Et le rejeu du même lot (même change_id) est stable.
        var rejeu = _push.Traiter(Fabrique.Lot(Fabrique.AppareilB, changement));
        Assert.True(rejeu.Resultats[0].Rejoue);
        Assert.Equal(ResultatChangement.RefusePurge, rejeu.Resultats[0].Resultat);
    }

    // ----- Cascade pièces jointes (§7, D-010.4) -----

    [Fact]
    public void Purge_d_un_element_purge_ses_pieces_jointes()
    {
        var element = Fabrique.Facture();
        var pieceDeLElement = NouvellePiece(element.Id);
        var pieceAutreElement = NouvellePiece(Guid.NewGuid());
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(element, EntiteSynchro.Element),
            Fabrique.Changement(pieceDeLElement, EntiteSynchro.PieceJointe),
            Fabrique.Changement(pieceAutreElement, EntiteSynchro.PieceJointe)));

        var suppression = Fabrique.Copier(element);
        suppression.Supprime = true;
        suppression.DateSuppression = element.DateModification.AddHours(1);
        suppression.DateModification = element.DateModification.AddHours(1);
        suppression.Version = element.Version + 1;
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(suppression, EntiteSynchro.Element)));

        var demande = Demande(element.Id);
        var reponse = _purge.Traiter(Lot(demande));

        var induite = Assert.Single(reponse.PurgesInduites);
        Assert.Equal(pieceDeLElement.Id, induite.EntiteId);
        Assert.Equal(Uuid5.Derive($"purge:{demande.ChangeId:D}:{pieceDeLElement.Id:D}"), induite.ChangeId);

        Assert.Null(_magasin.Obtenir(EntiteSynchro.PieceJointe, pieceDeLElement.Id));
        Assert.NotNull(_magasin.ObtenirTombale(EntiteSynchro.PieceJointe, pieceDeLElement.Id));
        Assert.NotNull(_magasin.Obtenir(EntiteSynchro.PieceJointe, pieceAutreElement.Id)); // intacte
        Assert.Null(_magasin.ObtenirTombale(EntiteSynchro.PieceJointe, pieceAutreElement.Id));
    }

    [Fact]
    public void Purge_directe_d_une_piece_jointe_seule()
    {
        var element = Fabrique.Facture();
        var piece = NouvellePiece(element.Id);
        piece.Supprime = true;
        piece.DateSuppression = Fabrique.T0;
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(element, EntiteSynchro.Element),
            Fabrique.Changement(piece, EntiteSynchro.PieceJointe)));

        var reponse = _purge.Traiter(Lot(Demande(piece.Id, EntiteSynchro.PieceJointe)));

        Assert.Equal(StatutPurge.Purgee, reponse.Resultats[0].Statut);
        Assert.Null(_magasin.Obtenir(EntiteSynchro.PieceJointe, piece.Id));
        Assert.NotNull(_magasin.Obtenir(EntiteSynchro.Element, element.Id)); // le parent reste
    }

    private static PieceJointe NouvellePiece(Guid elementId)
        => new()
        {
            Id = Guid.NewGuid(),
            ElementId = elementId,
            NomFichier = "facture.pdf",
            TailleOctets = 1234,
            BlobPath = "blobs/x",
            DateCreation = Fabrique.T0,
            DateModification = Fabrique.T0,
            AppareilSource = Fabrique.AppareilA,
            Version = 1,
        };

    // ----- Lot mixte et pull fusionné -----

    [Fact]
    public void Lot_mixte_refus_et_purges_tous_traites()
    {
        var purgeable = CreerPuisSupprimer();
        var reponse = _purge.Traiter(Lot(Demande(Guid.NewGuid()), Demande(purgeable.Id)));

        Assert.Equal(StatutPurge.Refusee, reponse.Resultats[0].Statut); // un refus n'est pas une erreur
        Assert.Equal(StatutPurge.Purgee, reponse.Resultats[1].Statut);
    }

    [Fact]
    public void Pull_fusionne_etats_et_purges_par_server_seq_avec_pagination()
    {
        var purgee = CreerPuisSupprimer();                       // seq 1, 2
        _purge.Traiter(Lot(Demande(purgee.Id)));                 // seq 3 (tombale)
        var vivante = Fabrique.Facture(titre: "Vivante");
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(vivante, EntiteSynchro.Element))); // seq 4

        var page1 = _pull.Traiter(0, limite: 1);
        Assert.True(page1.Encore);
        Assert.Empty(page1.Entites); // seq 1 et 2 : l'état de « purgee » n'existe plus — première page = tombale
        var tombale = Assert.Single(page1.Purges);
        Assert.Equal(purgee.Id, tombale.Id);
        Assert.Equal(3, page1.Curseur);

        var page2 = _pull.Traiter(page1.Curseur, limite: 10);
        Assert.False(page2.Encore);
        var etat = Assert.Single(page2.Entites);
        Assert.Equal(vivante.Id, etat.Id);
        Assert.Empty(page2.Purges);
        Assert.Equal(4, page2.Curseur);
    }
}
