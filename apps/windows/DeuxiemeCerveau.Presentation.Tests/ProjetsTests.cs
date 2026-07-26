using DeuxiemeCerveau.Core.Modele;
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

        modele.SupprimerProjetCommand.Execute(modele.Projets[0]);
        coquille.Rafraichir();

        Assert.Empty(modele.Projets);
        Assert.True(modele.AucunProjet);
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
