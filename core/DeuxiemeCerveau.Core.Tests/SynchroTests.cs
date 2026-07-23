using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;
using Xunit;

namespace DeuxiemeCerveau.Core.Tests;

/// <summary>
/// Le protocole §6.2, mot pour mot : idempotence, atomicité, arbitrage avec archivage des perdants,
/// server_seq croissant, pull par curseur. Le morceau le plus risqué du projet (§6, vigilance Chemin B).
/// </summary>
public class ProcesseurPushTests
{
    private readonly MagasinSynchroMemoire _magasin = new();
    private readonly ProcesseurPush _push;

    public ProcesseurPushTests()
        => _push = new ProcesseurPush(_magasin, new HorlogeFixe(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero)));

    private Element EtatElement(Guid id)
        => SerialisationCanonique.Deserialiser<Element>(
            _magasin.Obtenir(EntiteSynchro.Element, id)!.PayloadCanonique);

    // ----- Application nominale -----

    [Fact]
    public void Creation_appliquee_avec_server_seq()
    {
        var facture = Fabrique.Facture();
        var reponse = _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(facture, EntiteSynchro.Element)));

        var resultat = Assert.Single(reponse.Resultats);
        Assert.Equal(ResultatChangement.Applique, resultat.Resultat);
        Assert.False(resultat.Conflit);
        Assert.Equal(1, resultat.ServerSeq);

        var etat = _magasin.Obtenir(EntiteSynchro.Element, facture.Id);
        Assert.NotNull(etat);
        Assert.Equal(1, etat.Version);
        Assert.Equal(1, etat.ServerSeq);
        Assert.Equal(facture.Titre, EtatElement(facture.Id).Titre);
    }

    [Fact]
    public void Creation_puis_edition_dans_le_meme_lot_ordonne()
    {
        var facture = Fabrique.Facture();
        var v2 = Fabrique.Copier(facture);
        v2.Titre = "Loyer révisé";
        v2.Version = 2;
        v2.DateModification = facture.DateModification.AddMinutes(1);

        var reponse = _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(facture, EntiteSynchro.Element),
            Fabrique.Changement(v2, EntiteSynchro.Element)));

        Assert.All(reponse.Resultats, r => Assert.Equal(ResultatChangement.Applique, r.Resultat));
        Assert.Equal("Loyer révisé", EtatElement(facture.Id).Titre);
        Assert.Equal(2, _magasin.Obtenir(EntiteSynchro.Element, facture.Id)!.Version);
    }

    // ----- Idempotence (§6.2.1, NON NÉGOCIABLE) : « double envoi d'un même lot » -----

    [Fact]
    public void Renvoyer_deux_fois_le_meme_lot_donne_le_meme_etat()
    {
        var facture = Fabrique.Facture();
        var lot = Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(facture, EntiteSynchro.Element));

        var premiere = _push.Traiter(lot);
        var etatApres = _magasin.Obtenir(EntiteSynchro.Element, facture.Id);
        var journalApres = _magasin.Journal.Count;
        var seqApres = _magasin.SeqCourante;

        var seconde = _push.Traiter(lot); // coupure réseau simulée : le lot repart tel quel

        Assert.Equal(etatApres, _magasin.Obtenir(EntiteSynchro.Element, facture.Id)); // état identique
        Assert.Equal(journalApres, _magasin.Journal.Count);                            // journal inchangé
        Assert.Equal(seqApres, _magasin.SeqCourante);                                  // aucune seq consommée
        var resultat = Assert.Single(seconde.Resultats);
        Assert.True(resultat.Rejoue);
        Assert.Equal(premiere.Resultats[0].ServerSeq, resultat.ServerSeq); // même réponse qu'à l'origine
        Assert.Equal(premiere.Resultats[0].Resultat, resultat.Resultat);
    }

    [Fact]
    public void Lot_partiellement_rejoue_napplique_que_le_nouveau()
    {
        var facture = Fabrique.Facture();
        var changement1 = Fabrique.Changement(facture, EntiteSynchro.Element);
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, changement1));

        var v2 = Fabrique.Copier(facture);
        v2.Titre = "Après coupure";
        v2.Version = 2;
        v2.DateModification = facture.DateModification.AddMinutes(2);
        var changement2 = Fabrique.Changement(v2, EntiteSynchro.Element);

        // L'app n'a pas reçu la confirmation : elle renvoie l'ancien changement AVEC le nouveau.
        var reponse = _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, changement1, changement2));

        Assert.True(reponse.Resultats[0].Rejoue);
        Assert.False(reponse.Resultats[1].Rejoue);
        Assert.Equal("Après coupure", EtatElement(facture.Id).Titre);
        Assert.Equal(2, _magasin.Journal.Count); // aucun doublon au journal
    }

    // ----- Arbitrage (§6.2.3, D-005) : dernière écriture gagne, perdant archivé -----

    private (Element aDeA, Element aDeB) PreparerConflit(
        DateTimeOffset modificationA, DateTimeOffset modificationB)
    {
        // Base commune v1 poussée par A, puis modifications concurrentes v2 sur A et sur B hors-ligne.
        var origine = Fabrique.Facture(dateModification: Fabrique.T0);
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(origine, EntiteSynchro.Element)));

        var deA = Fabrique.Copier(origine);
        deA.Titre = "Version de A";
        deA.Version = 2;
        deA.DateModification = modificationA;

        var deB = Fabrique.Copier(origine);
        deB.Titre = "Version de B";
        deB.Version = 2;
        deB.DateModification = modificationB;
        deB.AppareilSource = Fabrique.AppareilB;

        return (deA, deB);
    }

    [Fact]
    public void Conflit_la_derniere_ecriture_gagne()
    {
        var (deA, deB) = PreparerConflit(
            modificationA: Fabrique.T0.AddMinutes(10),
            modificationB: Fabrique.T0.AddMinutes(20)); // B écrit en dernier

        _push.Traiter(Fabrique.Lot(Fabrique.AppareilB, Fabrique.Changement(deB, EntiteSynchro.Element)));
        var reponse = _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(deA, EntiteSynchro.Element)));

        // A arrive après mais a écrit avant : A perd, l'état reste celui de B.
        var resultat = Assert.Single(reponse.Resultats);
        Assert.Equal(ResultatChangement.PerdantArchive, resultat.Resultat);
        Assert.True(resultat.Conflit);
        Assert.Equal("Version de B", EtatElement(deB.Id).Titre);

        // Filet 3 : la version perdante est intégralement au journal, récupérable.
        var perdant = _magasin.Journal.Single(j => j.Resultat == ResultatChangement.PerdantArchive);
        var payloadPerdant = SerialisationCanonique.Deserialiser<Element>(perdant.Payload);
        Assert.Equal("Version de A", payloadPerdant.Titre);
    }

    [Fact]
    public void Conflit_gagne_par_l_entrant_version_ne_regresse_jamais()
    {
        var (deA, deB) = PreparerConflit(
            modificationA: Fabrique.T0.AddMinutes(30), // A écrit en dernier…
            modificationB: Fabrique.T0.AddMinutes(20));

        _push.Traiter(Fabrique.Lot(Fabrique.AppareilB, Fabrique.Changement(deB, EntiteSynchro.Element)));
        var reponse = _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(deA, EntiteSynchro.Element)));

        // …donc A gagne malgré son arrivée tardive.
        var resultat = Assert.Single(reponse.Resultats);
        Assert.Equal(ResultatChangement.Applique, resultat.Resultat);
        Assert.True(resultat.Conflit);
        Assert.Equal("Version de A", EtatElement(deA.Id).Titre);
        // Version serveur : max(2, 2+1) = 3 — le compteur ne régresse jamais (D-005).
        Assert.Equal(3, _magasin.Obtenir(EntiteSynchro.Element, deA.Id)!.Version);

        // La version de B (écrasée) reste récupérable au journal, à son entrée d'origine.
        var entreeB = _magasin.Journal.Single(j =>
            j.Resultat == ResultatChangement.Applique
            && SerialisationCanonique.Deserialiser<Element>(j.Payload).Titre == "Version de B");
        Assert.Equal(Fabrique.AppareilB, entreeB.AppareilId);
    }

    [Fact]
    public void Conflit_a_egalite_stricte_le_premier_arrive_gagne()
    {
        var instant = Fabrique.T0.AddMinutes(10);
        var (deA, deB) = PreparerConflit(modificationA: instant, modificationB: instant);

        _push.Traiter(Fabrique.Lot(Fabrique.AppareilB, Fabrique.Changement(deB, EntiteSynchro.Element)));
        var reponse = _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(deA, EntiteSynchro.Element)));

        Assert.Equal(ResultatChangement.PerdantArchive, reponse.Resultats[0].Resultat);
        Assert.Equal("Version de B", EtatElement(deB.Id).Titre); // l'ordre d'arrivée serveur tranche (D-005)
    }

    [Fact]
    public void Suppression_et_edition_concurrentes_arbitrees_comme_le_reste()
    {
        var (deA, deB) = PreparerConflit(
            modificationA: Fabrique.T0.AddMinutes(10),
            modificationB: Fabrique.T0.AddMinutes(20));
        deB.Supprime = true; // B supprime (marquage, filet 2), A édite — B a écrit en dernier
        deB.DateSuppression = deB.DateModification;

        _push.Traiter(Fabrique.Lot(Fabrique.AppareilB, Fabrique.Changement(deB, EntiteSynchro.Element)));
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(deA, EntiteSynchro.Element)));

        var etat = _magasin.Obtenir(EntiteSynchro.Element, deB.Id)!;
        Assert.True(etat.Supprime); // la suppression gagne (dernière écriture)

        // Restauration depuis la corbeille (§5.6) : une édition postérieure remet supprime = false.
        var restauration = Fabrique.Copier(deB);
        restauration.Supprime = false;
        restauration.DateSuppression = null;
        restauration.Version = etat.Version + 1;
        restauration.DateModification = Fabrique.T0.AddMinutes(30);
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilB, Fabrique.Changement(restauration, EntiteSynchro.Element)));

        Assert.False(_magasin.Obtenir(EntiteSynchro.Element, deB.Id)!.Supprime);
    }

    // ----- Atomicité (§6.2.2) -----

    [Fact]
    public void Lot_invalide_rejete_entierement_rien_applique()
    {
        var valide = Fabrique.Facture();
        var invalide = Fabrique.Facture();
        invalide.Statut = StatutElement.Fait; // statut interdit pour une facture (§3.1)

        var erreur = Assert.Throws<ErreurLotInvalide>(() => _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(valide, EntiteSynchro.Element),
            Fabrique.Changement(invalide, EntiteSynchro.Element))));

        Assert.Contains(erreur.Erreurs, e => e.Erreurs.Any(v => v.Code == "statut_interdit"));
        Assert.Null(_magasin.Obtenir(EntiteSynchro.Element, valide.Id)); // le changement valide n'est PAS appliqué
        Assert.Empty(_magasin.Journal);
        Assert.Equal(0, _magasin.SeqCourante);
    }

    [Fact]
    public void Enveloppe_incoherente_avec_le_payload_rejetee()
    {
        var facture = Fabrique.Facture();
        var changement = Fabrique.Changement(facture, EntiteSynchro.Element);
        changement.Version = 7; // ne correspond pas au payload

        var erreur = Assert.Throws<ErreurLotInvalide>(
            () => _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, changement)));
        Assert.Contains(erreur.Erreurs[0].Erreurs, v => v.Code == "enveloppe_incoherente");
    }

    [Fact]
    public void Montant_flottant_rejete_des_la_deserialisation()
    {
        var facture = Fabrique.Facture();
        var changement = Fabrique.Changement(facture, EntiteSynchro.Element);
        var json = changement.Payload.GetRawText().Replace("80000", "800.55");
        changement.Payload = System.Text.Json.JsonDocument.Parse(json).RootElement;

        var erreur = Assert.Throws<ErreurLotInvalide>(
            () => _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, changement)));
        Assert.Contains(erreur.Erreurs[0].Erreurs, v => v.Code == "payload_invalide"); // règle 5
    }

    [Fact]
    public void Champ_inconnu_dans_le_payload_rejete_bruyamment()
    {
        var facture = Fabrique.Facture();
        var changement = Fabrique.Changement(facture, EntiteSynchro.Element);
        var json = changement.Payload.GetRawText().TrimEnd('}') + ",\"champ_mystere\":1}";
        changement.Payload = System.Text.Json.JsonDocument.Parse(json).RootElement;

        var erreur = Assert.Throws<ErreurLotInvalide>(
            () => _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, changement)));
        Assert.Contains(erreur.Erreurs[0].Erreurs, v => v.Code == "payload_invalide"); // D-007
    }

    // ----- Fermeture de projet (§3.2, D-006) -----

    [Fact]
    public void Fermeture_projet_reporte_les_taches_a_faire()
    {
        var projet = Fabrique.Projet();
        var aFaire = Fabrique.Tache(titre: "À faire", projetId: projet.Id);
        var faite = Fabrique.Tache(titre: "Faite", statut: StatutElement.Fait, projetId: projet.Id);
        var supprimee = Fabrique.Tache(titre: "Supprimée", projetId: projet.Id);
        supprimee.Supprime = true;
        supprimee.DateSuppression = Fabrique.T0;
        var horsProjet = Fabrique.Tache(titre: "Hors projet");

        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(projet, EntiteSynchro.Projet),
            Fabrique.Changement(aFaire, EntiteSynchro.Element),
            Fabrique.Changement(faite, EntiteSynchro.Element),
            Fabrique.Changement(supprimee, EntiteSynchro.Element),
            Fabrique.Changement(horsProjet, EntiteSynchro.Element)));

        var fermeture = Fabrique.Projet(id: projet.Id, statut: StatutProjet.Termine, version: 2,
            dateModification: Fabrique.T0.AddHours(1));
        var reponse = _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(fermeture, EntiteSynchro.Projet)));

        // Seule la tâche « a_faire » non supprimée du projet est reportée — rien n'est perdu,
        // rien ne pollue les vues actives (§3.2).
        var induit = Assert.Single(reponse.ChangementsInduits);
        Assert.Equal(aFaire.Id, induit.EntiteId);

        Assert.Equal(StatutElement.Reporte, EtatElement(aFaire.Id).Statut);
        Assert.Equal(2, EtatElement(aFaire.Id).Version);
        Assert.Equal(fermeture.DateModification, EtatElement(aFaire.Id).DateModification);
        Assert.Equal(StatutElement.Fait, EtatElement(faite.Id).Statut);
        Assert.Equal(StatutElement.AFaire, EtatElement(supprimee.Id).Statut);
        Assert.Equal(StatutElement.AFaire, EtatElement(horsProjet.Id).Statut);
    }

    [Fact]
    public void Fermeture_projet_rejouee_sans_double_effet()
    {
        var projet = Fabrique.Projet();
        var tache = Fabrique.Tache(projetId: projet.Id);
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(projet, EntiteSynchro.Projet),
            Fabrique.Changement(tache, EntiteSynchro.Element)));

        var fermeture = Fabrique.Projet(id: projet.Id, statut: StatutProjet.EnPause, version: 2,
            dateModification: Fabrique.T0.AddHours(1));
        var lot = Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(fermeture, EntiteSynchro.Projet));

        _push.Traiter(lot);
        var journalApres = _magasin.Journal.Count;
        var rejeu = _push.Traiter(lot); // coupure réseau : le lot repart

        Assert.Equal(journalApres, _magasin.Journal.Count); // ni double cascade, ni double application
        Assert.Empty(rejeu.ChangementsInduits);
        Assert.Equal(StatutElement.Reporte, EtatElement(tache.Id).Statut);
        Assert.Equal(2, EtatElement(tache.Id).Version);
    }

    [Fact]
    public void Nouvelle_tache_sur_projet_ferme_reportee_a_la_fermeture_suivante()
    {
        var projet = Fabrique.Projet();
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(projet, EntiteSynchro.Projet)));
        var fermeture = Fabrique.Projet(id: projet.Id, statut: StatutProjet.Termine, version: 2,
            dateModification: Fabrique.T0.AddHours(1));
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(fermeture, EntiteSynchro.Projet)));

        // Une tâche créée ensuite sur le projet fermé (autre appareil pas au courant, par exemple).
        var retardataire = Fabrique.Tache(titre: "Retardataire", projetId: projet.Id,
            dateModification: Fabrique.T0.AddHours(2));
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(retardataire, EntiteSynchro.Element)));
        Assert.Equal(StatutElement.AFaire, EtatElement(retardataire.Id).Statut);

        // Une nouvelle application du projet fermé (édition quelconque) reporte la retardataire.
        var edition = Fabrique.Projet(id: projet.Id, nom: "MMA (archivé)", statut: StatutProjet.Termine,
            version: 3, dateModification: Fabrique.T0.AddHours(3));
        var reponse = _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(edition, EntiteSynchro.Projet)));

        Assert.Single(reponse.ChangementsInduits);
        Assert.Equal(StatutElement.Reporte, EtatElement(retardataire.Id).Statut);
    }

    // ----- Taille de lot -----

    [Fact]
    public void Lot_trop_grand_rejete()
    {
        var lot = new LotPush { AppareilId = Fabrique.AppareilA };
        for (var i = 0; i <= ProcesseurPush.TailleLotMax; i++)
            lot.Changements.Add(Fabrique.Changement(Fabrique.Facture(), EntiteSynchro.Element));

        Assert.Throws<ErreurLotInvalide>(() => _push.Traiter(lot));
        Assert.Empty(_magasin.Journal);
    }
}

public class ProcesseurPullTests
{
    private readonly MagasinSynchroMemoire _magasin = new();
    private readonly ProcesseurPush _push;
    private readonly ProcesseurPull _pull;

    public ProcesseurPullTests()
    {
        _push = new ProcesseurPush(_magasin, new HorlogeFixe(Fabrique.T0));
        _pull = new ProcesseurPull(_magasin);
    }

    [Fact]
    public void Pull_depuis_zero_renvoie_tout_puis_rien()
    {
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
            Fabrique.Changement(Fabrique.Facture(), EntiteSynchro.Element),
            Fabrique.Changement(Fabrique.Categorie(), EntiteSynchro.Categorie),
            Fabrique.Changement(Fabrique.Projet(), EntiteSynchro.Projet)));

        var page = _pull.Traiter(0);
        Assert.Equal(3, page.Entites.Count);
        Assert.False(page.Encore);
        Assert.Equal(3, page.Curseur);

        var vide = _pull.Traiter(page.Curseur);
        Assert.Empty(vide.Entites);
        Assert.Equal(page.Curseur, vide.Curseur); // le curseur EST le point de reprise (§6.2)
    }

    [Fact]
    public void Pull_pagine_avec_reprise_par_curseur()
    {
        for (var i = 0; i < 5; i++)
            _push.Traiter(Fabrique.Lot(Fabrique.AppareilA,
                Fabrique.Changement(Fabrique.Facture(titre: $"F{i}"), EntiteSynchro.Element)));

        var page1 = _pull.Traiter(0, limite: 2);
        Assert.Equal(2, page1.Entites.Count);
        Assert.True(page1.Encore);
        Assert.Equal(2, page1.Curseur); // dernier server_seq renvoyé — reprise exacte après coupure

        var page2 = _pull.Traiter(page1.Curseur, limite: 2);
        var page3 = _pull.Traiter(page2.Curseur, limite: 2);
        Assert.Single(page3.Entites);
        Assert.False(page3.Encore);
        Assert.Equal(5, page3.Curseur);
    }

    [Fact]
    public void Pull_renvoie_les_suppressions_marquees()
    {
        var facture = Fabrique.Facture();
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(facture, EntiteSynchro.Element)));
        var curseurAvant = _pull.Traiter(0).Curseur;

        var suppression = Fabrique.Copier(facture);
        suppression.Supprime = true;
        suppression.DateSuppression = Fabrique.T0.AddHours(1);
        suppression.Version = 2;
        suppression.DateModification = Fabrique.T0.AddHours(1);
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(suppression, EntiteSynchro.Element)));

        var page = _pull.Traiter(curseurAvant);
        var entite = Assert.Single(page.Entites);
        Assert.True(entite.Supprime); // l'autre appareil doit voir la corbeille (§5.6, filet 2)
    }

    [Fact]
    public void Pull_apres_perdant_archive_avance_le_curseur_sans_rien_renvoyer()
    {
        var origine = Fabrique.Facture(dateModification: Fabrique.T0);
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(origine, EntiteSynchro.Element)));

        var recente = Fabrique.Copier(origine);
        recente.Version = 2;
        recente.DateModification = Fabrique.T0.AddMinutes(20);
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilA, Fabrique.Changement(recente, EntiteSynchro.Element)));

        var curseur = _pull.Traiter(0).Curseur; // = 2

        var perdante = Fabrique.Copier(origine);
        perdante.Titre = "Perdante";
        perdante.Version = 2;
        perdante.DateModification = Fabrique.T0.AddMinutes(10); // plus ancienne → archivée
        perdante.AppareilSource = Fabrique.AppareilB;
        _push.Traiter(Fabrique.Lot(Fabrique.AppareilB, Fabrique.Changement(perdante, EntiteSynchro.Element)));

        var page = _pull.Traiter(curseur);
        Assert.Empty(page.Entites); // rien à appliquer…
        Assert.Equal(3, page.Curseur); // …mais le curseur absorbe la seq du perdant
    }
}

/// <summary>Recalage du solde de référence (§3.4) : dernier recalage gagne, historique au journal.</summary>
public class ProcesseurReglageTests
{
    private readonly MagasinSynchroMemoire _magasin = new();
    private readonly ProcesseurReglage _reglage;

    public ProcesseurReglageTests()
        => _reglage = new ProcesseurReglage(_magasin, new HorlogeFixe(Fabrique.T0));

    private static RecalageSolde Recalage(long centimes, DateOnly date, DateTimeOffset modification,
        Guid? appareil = null, Guid? changeId = null)
        => new()
        {
            ChangeId = changeId ?? Guid.NewGuid(),
            SoldeReferenceCentimes = centimes,
            SoldeReferenceDate = date,
            DateModification = modification,
            AppareilId = appareil ?? Fabrique.AppareilA,
        };

    [Fact]
    public void Premier_recalage_cree_le_reglage()
    {
        var resultat = _reglage.Recaler(Recalage(150000, new DateOnly(2026, 7, 1), Fabrique.T0));
        Assert.Equal(ResultatChangement.Applique, resultat.Resultat);

        var solde = _reglage.Lire();
        Assert.NotNull(solde);
        Assert.Equal(150000, solde.SoldeReferenceCentimes);
        Assert.Equal(new DateOnly(2026, 7, 1), solde.SoldeReferenceDate);
    }

    [Fact]
    public void Dernier_recalage_gagne_historique_au_journal()
    {
        _reglage.Recaler(Recalage(100000, new DateOnly(2026, 7, 1), Fabrique.T0));
        _reglage.Recaler(Recalage(200000, new DateOnly(2026, 7, 10), Fabrique.T0.AddDays(9)));

        // Recalage retardataire (plus ancien par date_modification) : perd, archivé.
        var retard = _reglage.Recaler(Recalage(999999, new DateOnly(2026, 7, 5),
            Fabrique.T0.AddDays(4), appareil: Fabrique.AppareilB));

        Assert.Equal(ResultatChangement.PerdantArchive, retard.Resultat);
        Assert.Equal(200000, _reglage.Lire()!.SoldeReferenceCentimes);
        Assert.Equal(3, _magasin.Journal.Count); // historique complet conservé (§3.4)
    }

    [Fact]
    public void Recalage_idempotent_par_change_id()
    {
        var changeId = Guid.NewGuid();
        var recalage = Recalage(100000, new DateOnly(2026, 7, 1), Fabrique.T0, changeId: changeId);
        _reglage.Recaler(recalage);
        var rejeu = _reglage.Recaler(recalage);

        Assert.True(rejeu.Rejoue);
        Assert.Single(_magasin.Journal);
        Assert.Equal(1, _magasin.Obtenir(EntiteSynchro.Reglage, ReglageSolde.IdSoldeReference)!.Version);
    }

    [Fact]
    public void Solde_negatif_autorise()
    {
        _reglage.Recaler(Recalage(-50000, new DateOnly(2026, 7, 1), Fabrique.T0));
        Assert.Equal(-50000, _reglage.Lire()!.SoldeReferenceCentimes); // découvert réel (D-004)
    }
}
