using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Projets personnels et leurs tâches (§5.3, entrés en V1 par D-027), et le calendrier de projet
/// qui devient automatiquement un filtre (§5.4).
/// </summary>
public class ProjetsTests
{
    private static VueModeleProjets Creer(FabriquePresentation f, params string[] noms)
    {
        var modele = new VueModeleProjets(f.Composition);
        foreach (var nom in noms)
        {
            modele.NouveauNom = nom;
            modele.CreerCommand.Execute(null);
        }
        return modele;
    }

    [Fact]
    public void Creer_un_projet_cree_AUSSI_son_calendrier()
    {
        // §5.4 : « calendrier dédié devenant filtre automatique ». Un projet dont le filtre
        // resterait à créer à la main ne serait pas automatique.
        using var f = new FabriquePresentation();

        Creer(f, "Commencer le MMA");

        var categorie = Assert.Single(
            f.Composition.Acces.Lire(() => f.Composition.Lecture.Categories()));
        Assert.Equal("Commencer le MMA", categorie.Nom);
        Assert.Equal(OrigineCategorie.Projet, categorie.Origine);

        // Le lien projet → calendrier doit être RÉEL. Il s'est déjà cassé en silence une fois :
        // l'Id d'une entité neuve est vide tant qu'Enregistrer ne l'a pas posé.
        var projet = Assert.Single(f.Composition.Acces.Lire(() => f.Composition.Lecture.Projets()));
        Assert.Equal(categorie.Id, projet.CategorieId);
    }

    [Fact]
    public void Le_calendrier_d_un_projet_est_range_a_part_dans_la_barre_laterale()
    {
        // Il ne se gère pas comme une catégorie faite à la main : on ne le crée ni ne le supprime,
        // il suit son projet. Les mélanger rendrait la section « Mes calendriers » ingérable.
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Maison");

        var coquille = f.Coquille();
        Creer(f, "Arrêter de fumer");
        coquille.Rafraichir();

        Assert.Equal("Maison", Assert.Single(coquille.Calendriers).Nom);
        Assert.Equal("Arrêter de fumer", Assert.Single(coquille.CalendriersProjets).Nom);
        Assert.True(coquille.AProjetsAuCalendrier);
    }

    [Fact]
    public void Une_tache_se_cree_dans_le_projet_ouvert()
    {
        using var f = new FabriquePresentation();
        var modele = Creer(f, "Étudier pour les examens");

        modele.NouvelleTache = "Réviser le chapitre 3";
        modele.AjouterTacheCommand.Execute(null);

        var projet = Assert.Single(modele.Projets);
        var tache = Assert.Single(projet.Taches);
        Assert.Equal("Réviser le chapitre 3", tache.Titre);
        Assert.False(tache.Faite);
    }

    [Fact]
    public void Une_tache_creee_ici_porte_toujours_son_projet()
    {
        // La frontière V1/V2 (D-027) : le cœur refuse une tâche sans projet. Si la vue en créait
        // une, le refus serait silencieux et la tâche disparaîtrait sans un mot.
        using var f = new FabriquePresentation();
        var modele = Creer(f, "Sport");
        modele.NouvelleTache = "Séance du lundi";
        modele.AjouterTacheCommand.Execute(null);

        Assert.Null(modele.Message);

        var tache = Assert.Single(
            f.Composition.Acces.Lire(() => f.Composition.Lecture.Actifs(TypeElement.Tache)));
        Assert.Equal(modele.Projets[0].Id, tache.ProjetId);
    }

    [Fact]
    public void Cocher_une_tache_la_marque_faite_et_l_avancement_suit()
    {
        using var f = new FabriquePresentation();
        var modele = Creer(f, "Sport");
        modele.NouvelleTache = "Séance";
        modele.AjouterTacheCommand.Execute(null);

        modele.BasculerTacheCommand.Execute(modele.Projets[0].Taches[0]);

        Assert.True(modele.Projets[0].Taches[0].Faite);
        Assert.Contains("1 faite", modele.Projets[0].Avancement);
    }

    [Fact]
    public void La_priorite_tourne_sur_trois_valeurs()
    {
        using var f = new FabriquePresentation();
        var modele = Creer(f, "Sport");
        modele.NouvelleTache = "Séance";
        modele.AjouterTacheCommand.Execute(null);

        Assert.Equal(Priorite.Normale, modele.Projets[0].Taches[0].Priorite);

        modele.CyclerPrioriteCommand.Execute(modele.Projets[0].Taches[0]);
        Assert.Equal(Priorite.Haute, modele.Projets[0].Taches[0].Priorite);

        modele.CyclerPrioriteCommand.Execute(modele.Projets[0].Taches[0]);
        Assert.Equal(Priorite.Basse, modele.Projets[0].Taches[0].Priorite);
    }

    [Fact]
    public void Fermer_un_projet_eteint_son_filtre_de_calendrier()
    {
        // §3.2 : « son calendrier-filtre reste disponible, désactivé par défaut ».
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();
        var modele = new VueModeleProjets(f.Composition) { ApresChangement = coquille.Rafraichir };
        modele.NouveauNom = "Projet fini";
        modele.CreerCommand.Execute(null);
        coquille.Rafraichir();

        Assert.True(Assert.Single(coquille.CalendriersProjets).Visible);

        // Actif → en pause
        modele.ChangerStatutCommand.Execute(modele.Projets[0]);
        Assert.Null(modele.Message);
        Assert.Equal(StatutProjet.EnPause, modele.Projets[0].Statut);
        coquille.Rafraichir();

        Assert.False(Assert.Single(coquille.CalendriersProjets).Visible);
    }

    [Fact]
    public void Un_projet_supprime_emporte_son_filtre_hors_de_la_liste()
    {
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();
        var modele = new VueModeleProjets(f.Composition) { ApresChangement = coquille.Rafraichir };
        modele.NouveauNom = "Éphémère";
        modele.CreerCommand.Execute(null);
        coquille.Rafraichir();
        Assert.Single(coquille.CalendriersProjets);

        modele.SupprimerProjetCommand.Execute(modele.Projets[0]);
        coquille.Rafraichir();

        Assert.Empty(modele.Projets);
        Assert.True(modele.AucunProjet);

        // Le titre de ce test le promettait déjà ; le filtre, lui, restait. C'est le défaut signalé
        // à l'usage : « j'ai supprimé un projet mais je vois encore sa section » (§5.3, D-030).
        Assert.Empty(coquille.CalendriersProjets);
    }

    [Fact]
    public void Supprimer_une_tache_la_met_a_la_corbeille_et_ne_la_detruit_pas()
    {
        // Filet 2 : marquage, jamais destruction.
        using var f = new FabriquePresentation();
        var modele = Creer(f, "Sport");
        modele.NouvelleTache = "Séance";
        modele.AjouterTacheCommand.Execute(null);

        modele.SupprimerTacheCommand.Execute(modele.Projets[0].Taches[0]);

        Assert.Empty(modele.Projets[0].Taches);
        Assert.Single(f.Composition.Acces.Lire(() => f.Composition.Lecture.Corbeille()));
    }

    [Fact]
    public void La_zone_Projets_n_est_plus_etiquetee_V2()
    {
        // D-027 : l'étiquette et l'inertie tombent ensemble. Une zone visible mais morte est pire
        // qu'une zone absente.
        var projets = Assert.Single(ElementNav.Principales(), n => n.Zone == Zone.Projets);

        Assert.False(projets.EstV2);
    }
}

/// <summary>
/// Les tâches doivent rester OÙ ON LES A MISES. Défaut signalé par l'utilisateur : « quand j'appuie
/// sur la touche un, ça sélectionne le trois » — la liste était triée par titre et re-triée par
/// priorité, donc les lignes bougeaient sous le doigt.
/// </summary>
public class OrdreDesTachesTests
{
    private static VueModeleProjets AvecTaches(FabriquePresentation f, params string[] titres)
    {
        var modele = new VueModeleProjets(f.Composition);
        modele.NouveauNom = "Commencer le MMA";
        modele.CreerCommand.Execute(null);
        foreach (var t in titres)
        {
            modele.NouvelleTache = t;
            modele.AjouterTacheCommand.Execute(null);
        }
        return modele;
    }

    [Fact]
    public void Les_taches_restent_dans_l_ordre_d_ajout_pas_dans_l_ordre_alphabetique()
    {
        using var f = new FabriquePresentation();
        // Volontairement à contre-sens de l'alphabet : « Trouver » d'abord, « Acheter » ensuite.
        var modele = AvecTaches(f, "Trouver un club", "Acheter des gants", "Premier cours");

        var titres = modele.ProjetOuvert!.Taches.Select(t => t.Titre).ToList();

        Assert.Equal(["Trouver un club", "Acheter des gants", "Premier cours"], titres);
    }

    [Fact]
    public void Cocher_une_tache_ne_deplace_aucune_ligne()
    {
        using var f = new FabriquePresentation();
        var modele = AvecTaches(f, "Trouver un club", "Acheter des gants", "Premier cours");
        var avant = modele.ProjetOuvert!.Taches.Select(t => t.Id).ToList();
        var premiere = modele.ProjetOuvert!.Taches[0];

        modele.BasculerTacheCommand.Execute(premiere);

        // Même ordre, mêmes objets : la liste n'a pas été reconstruite sous le clic.
        Assert.Equal(avant, modele.ProjetOuvert!.Taches.Select(t => t.Id));
        Assert.True(modele.ProjetOuvert!.Taches[0].Faite);
        Assert.Same(premiere, modele.ProjetOuvert!.Taches[0]);
    }

    [Fact]
    public void Changer_la_priorite_ne_deplace_aucune_ligne()
    {
        // C'était le pire des deux : le tri par priorité faisait SAUTER la ligne qu'on venait de
        // viser, si bien qu'un second clic touchait une autre tâche.
        using var f = new FabriquePresentation();
        var modele = AvecTaches(f, "Une", "Deux", "Trois");
        var avant = modele.ProjetOuvert!.Taches.Select(t => t.Titre).ToList();

        modele.CyclerPrioriteCommand.Execute(modele.ProjetOuvert!.Taches[0]);

        Assert.Equal(avant, modele.ProjetOuvert!.Taches.Select(t => t.Titre));
        Assert.Equal(Priorite.Haute, modele.ProjetOuvert!.Taches[0].Priorite);
    }

    [Fact]
    public void L_ordre_survit_a_un_rechargement()
    {
        using var f = new FabriquePresentation();
        var modele = AvecTaches(f, "Trouver un club", "Acheter des gants");
        var id = modele.Projets[0].Id;

        var relu = new VueModeleProjets(f.Composition);
        relu.OuvrirCommand.Execute(relu.Projets.Single(p => p.Id == id));

        Assert.Equal(["Trouver un club", "Acheter des gants"],
                     relu.ProjetOuvert!.Taches.Select(t => t.Titre));
    }

    [Fact]
    public void Cocher_met_l_avancement_a_jour_sans_recharger()
    {
        using var f = new FabriquePresentation();
        var modele = AvecTaches(f, "Une", "Deux");

        modele.BasculerTacheCommand.Execute(modele.ProjetOuvert!.Taches[0]);

        Assert.Contains("1 faite", modele.ProjetOuvert!.Avancement);
    }
}

/// <summary>
/// Le calendrier suit son projet (§5.3, D-030). Défaut signalé après usage réel : « quand l'on
/// supprime un projet il faut que son calendrier lié soit supprimé, car j'ai supprimé un projet mais
/// je vois encore sa section dans l'onglet calendrier ».
/// <para>
/// <c>Creer</c> crée deux entités ; il fallait donc que <c>SupprimerProjet</c> en supprime deux et
/// que renommer en renomme deux. La seule exception est la <b>fermeture</b>, et elle est voulue.
/// </para>
/// </summary>
public class CalendrierDeProjetTests
{
    private static VueModeleProjets AvecProjet(FabriquePresentation f, string nom)
    {
        var modele = new VueModeleProjets(f.Composition);
        modele.NouveauNom = nom;
        modele.CreerCommand.Execute(null);
        return modele;
    }

    private static IReadOnlyList<Categorie> Actives(FabriquePresentation f) =>
        f.Composition.Acces.Lire(() => f.Composition.Lecture.Categories());

    private static IReadOnlyList<Categorie> Corbeille(FabriquePresentation f) =>
        f.Composition.Acces.Lire(() => f.Composition.Lecture.CorbeilleCategories());

    [Fact]
    public void Supprimer_un_projet_met_AUSSI_son_calendrier_a_la_corbeille()
    {
        using var f = new FabriquePresentation();
        var modele = AvecProjet(f, "Éphémère");
        Assert.Single(Actives(f));

        modele.SupprimerProjetCommand.Execute(modele.Projets[0]);

        Assert.Null(modele.Message);
        Assert.Empty(Actives(f));

        // Filet 2 (règle 11) : marqué, jamais détruit — le calendrier est restaurable.
        var range = Assert.Single(Corbeille(f));
        Assert.Equal("Éphémère", range.Nom);
        Assert.True(range.Supprime);
    }

    [Fact]
    public void Fermer_un_projet_ne_supprime_PAS_son_calendrier()
    {
        // « Fermer n'est pas supprimer » (§5.3, D-030) : à la fermeture le calendrier reste, parce
        // que le projet et son histoire existent toujours. Il est seulement éteint par défaut.
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();
        var modele = new VueModeleProjets(f.Composition) { ApresChangement = coquille.Rafraichir };
        modele.NouveauNom = "Projet fini";
        modele.CreerCommand.Execute(null);

        modele.ChangerStatutCommand.Execute(modele.Projets[0]); // actif → en pause
        coquille.Rafraichir();

        Assert.Equal(StatutProjet.EnPause, modele.Projets[0].Statut);
        Assert.Single(Actives(f));
        Assert.Empty(Corbeille(f));

        // Toujours listé — mais éteint.
        Assert.False(Assert.Single(coquille.CalendriersProjets).Visible);
    }

    [Fact]
    public void Renommer_un_projet_renomme_son_calendrier()
    {
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();
        var modele = new VueModeleProjets(f.Composition) { ApresChangement = coquille.Rafraichir };
        modele.NouveauNom = "Commencer le MM";
        modele.CreerCommand.Execute(null);

        modele.ModifierCommand.Execute(null);
        Assert.True(modele.EnEdition);
        Assert.Equal("Commencer le MM", modele.NomEnCours);

        modele.NomEnCours = "Commencer le MMA";
        modele.EnregistrerModificationCommand.Execute(null);

        Assert.Null(modele.Message);
        Assert.False(modele.EnEdition);
        Assert.Equal("Commencer le MMA", modele.Projets[0].Nom);

        // Le filtre porte le nom du projet, sinon la liste des calendriers ment.
        Assert.Equal("Commencer le MMA", Assert.Single(Actives(f)).Nom);
        coquille.Rafraichir();
        Assert.Equal("Commencer le MMA", Assert.Single(coquille.CalendriersProjets).Nom);
    }

    [Fact]
    public void Recolorier_un_projet_recolorie_son_calendrier()
    {
        // La couleur identifie les occurrences du projet partout où elles apparaissent (§5.3) :
        // une pastille d'une couleur et un filtre d'une autre désignent deux choses différentes.
        using var f = new FabriquePresentation();
        var modele = AvecProjet(f, "Sport");
        var autre = modele.Couleurs.First(c => c != modele.Projets[0].Couleur);

        modele.ModifierCommand.Execute(null);
        modele.ChoisirCouleurCommand.Execute(autre);
        modele.EnregistrerModificationCommand.Execute(null);

        Assert.Equal(autre, modele.Projets[0].Couleur);
        Assert.Equal(autre, Assert.Single(Actives(f)).Couleur);
    }

    [Fact]
    public void Un_nom_vide_est_refuse_et_ne_referme_pas_le_formulaire()
    {
        using var f = new FabriquePresentation();
        var modele = AvecProjet(f, "Sport");

        modele.ModifierCommand.Execute(null);
        modele.NomEnCours = "   ";
        modele.EnregistrerModificationCommand.Execute(null);

        Assert.NotNull(modele.Message);
        // Le formulaire reste ouvert : refermer effacerait la frappe pour la même erreur, deux fois.
        Assert.True(modele.EnEdition);
        Assert.Equal("Sport", modele.Projets[0].Nom);
        Assert.Equal("Sport", Assert.Single(Actives(f)).Nom);
    }

    [Fact]
    public void Changer_de_projet_referme_le_formulaire()
    {
        using var f = new FabriquePresentation();
        var modele = AvecProjet(f, "Sport");
        modele.NouveauNom = "Études";
        modele.CreerCommand.Execute(null);

        modele.ModifierCommand.Execute(null);
        Assert.True(modele.EnEdition);

        modele.OuvrirCommand.Execute(modele.Projets.First(p => p.Nom == "Sport"));

        Assert.False(modele.EnEdition);
    }

    [Fact]
    public void Un_calendrier_deja_orphelin_cesse_d_apparaitre_au_demarrage_suivant()
    {
        // Le rattrapage : l'utilisateur a DÉJÀ supprimé des projets du temps où seul le projet
        // partait. Les calendriers restés derrière n'ont plus rien à filtrer et ne se gèrent pas à
        // la main (§5.4) — il n'existe donc aucun geste pour s'en défaire.
        using var f = new FabriquePresentation();
        var modele = AvecProjet(f, "Ancien projet");
        var idProjet = modele.Projets[0].Id;

        // On rejoue le DÉFAUT tel qu'il se produisait : seul le projet part.
        f.Composition.Acces.Lire(() => f.Composition.Saisie.Supprimer(EntiteSynchro.Projet, idProjet));
        Assert.Single(Actives(f));

        // Lancement suivant.
        var coquille = f.Coquille();

        Assert.Empty(coquille.CalendriersProjets);
        Assert.Empty(Actives(f));
        Assert.True(Assert.Single(Corbeille(f)).Supprime);

        // Une écriture que l'utilisateur n'a pas demandée se dit, et nomme la porte de retour.
        Assert.Contains("corbeille", coquille.Projets.MessageRattrapage);
    }

    [Fact]
    public void Le_rattrapage_epargne_les_calendriers_a_la_main_et_ceux_des_projets_fermes()
    {
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Maison");

        var modele = AvecProjet(f, "Projet en pause");
        modele.ChangerStatutCommand.Execute(modele.Projets[0]); // fermé, pas supprimé

        var relance = new VueModeleProjets(f.Composition);

        Assert.Null(relance.MessageRattrapage);
        Assert.Equal(2, Actives(f).Count);
        Assert.Empty(Corbeille(f));
    }

    [Fact]
    public void Le_rattrapage_n_ecrit_rien_quand_il_n_y_a_rien_a_ranger()
    {
        // Il est joué à chaque lancement : s'il écrivait à vide, il gonflerait l'outbox — donc le
        // trafic de synchro — à chaque ouverture de l'app, pour rien.
        using var f = new FabriquePresentation();
        AvecProjet(f, "Sport");
        var avant = f.Composition.Acces.Lire(() => f.Composition.Depot.Outbox().Count);

        var relance = new VueModeleProjets(f.Composition);

        Assert.Null(relance.MessageRattrapage);
        Assert.Equal(avant, f.Composition.Acces.Lire(() => f.Composition.Depot.Outbox().Count));
    }

    [Fact]
    public void Le_rattrapage_est_idempotent()
    {
        using var f = new FabriquePresentation();
        var modele = AvecProjet(f, "Ancien projet");
        f.Composition.Acces.Lire(
            () => f.Composition.Saisie.Supprimer(EntiteSynchro.Projet, modele.Projets[0].Id));

        var premier = new VueModeleProjets(f.Composition);
        Assert.NotNull(premier.MessageRattrapage);
        var apresRangement = f.Composition.Acces.Lire(() => f.Composition.Depot.Outbox().Count);

        var second = new VueModeleProjets(f.Composition);

        // Rangé une fois, l'orphelin n'est plus rendu par la lecture : la passe suivante n'a plus
        // rien à faire et n'écrit rien.
        Assert.Null(second.MessageRattrapage);
        Assert.Equal(apresRangement, f.Composition.Acces.Lire(() => f.Composition.Depot.Outbox().Count));
    }
}
