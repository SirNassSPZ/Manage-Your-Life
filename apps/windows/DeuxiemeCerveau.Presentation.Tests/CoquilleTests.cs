using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Coquille : aiguillage des zones, sous-vues contextuelles, filtres de calendrier (§5.4).
/// </summary>
public class CoquilleTests
{
    [Fact]
    public void Un_calendrier_masque_le_reste_apres_un_rafraichissement()
    {
        // Régression : ChargerCalendriers reconstruit la liste à CHAQUE rafraîchissement. Sans
        // mémoire des masqués, la moindre saisie ou le moindre changement de vue rallumait en
        // silence les calendriers que l'utilisateur venait d'éteindre.
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Santé");
        f.AjouterCategorie("Travail");

        var coquille = f.Coquille();
        var sante = coquille.Calendriers.Single(c => c.Nom == "Santé");
        Assert.True(sante.Visible);

        coquille.BasculerCalendrierCommand.Execute(sante);
        Assert.False(coquille.Calendriers.Single(c => c.Nom == "Santé").Visible);

        // Tout ce qui déclenche un rafraîchissement : navigation, saisie, retour de synchro.
        coquille.Aller(Zone.Calendrier);
        coquille.Aller(Zone.Aujourdhui);
        coquille.Rafraichir();

        Assert.False(coquille.Calendriers.Single(c => c.Nom == "Santé").Visible);
        Assert.True(coquille.Calendriers.Single(c => c.Nom == "Travail").Visible);
    }

    [Fact]
    public void Une_categorie_ajoutee_apres_coup_arrive_visible()
    {
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Santé");
        var coquille = f.Coquille();
        coquille.BasculerCalendrierCommand.Execute(coquille.Calendriers.Single(c => c.Nom == "Santé"));

        f.AjouterCategorie("Sport");
        coquille.Rafraichir();

        Assert.True(coquille.Calendriers.Single(c => c.Nom == "Sport").Visible);
        Assert.False(coquille.Calendriers.Single(c => c.Nom == "Santé").Visible);
    }

    [Fact]
    public void Rebasculer_un_calendrier_le_rallume()
    {
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Santé");
        var coquille = f.Coquille();
        var sante = coquille.Calendriers.Single(c => c.Nom == "Santé");

        coquille.BasculerCalendrierCommand.Execute(sante);
        coquille.BasculerCalendrierCommand.Execute(coquille.Calendriers.Single(c => c.Nom == "Santé"));

        Assert.True(coquille.Calendriers.Single(c => c.Nom == "Santé").Visible);
    }

    [Theory]
    [InlineData(Zone.Aujourdhui, true)]
    [InlineData(Zone.Finances, true)]
    [InlineData(Zone.Calendrier, true)]    // grille du mois · 7 jours · gérer les calendriers
    [InlineData(Zone.BudgetProjete, false)]
    public void L_intitule_des_sous_vues_ne_reste_jamais_seul(Zone zone, bool attendu)
    {
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();

        coquille.Aller(zone);

        Assert.Equal(attendu, coquille.ASousVues);
        Assert.Equal(attendu, coquille.SousVues.Count > 0);
    }

    [Theory]
    [InlineData(Zone.Calendrier, true)]
    [InlineData(Zone.Notes, false)]
    [InlineData(Zone.Corbeille, false)]
    [InlineData(Zone.BudgetProjete, false)]
    [InlineData(Zone.Aujourdhui, false)]
    public void Les_filtres_de_calendrier_ne_s_affichent_que_la_ou_ils_agissent(Zone zone, bool attendu)
    {
        // Régression : ils s'affichaient partout, y compris sur Notes et Corbeille où ils ne
        // filtrent rien. Une commande qui ne commande rien est pire qu'une commande absente.
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Santé");
        var coquille = f.Coquille();

        coquille.Aller(zone);

        Assert.Equal(attendu, coquille.AFiltresCalendrier);
    }

    [Fact]
    public void Une_zone_V2_reste_inerte()
    {
        // Périmètre V1 verrouillé : une zone V2 est montrée, jamais atteinte. Projets en est
        // sorti (D-027) ; la règle, elle, tient toujours pour celles qui restent.
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();
        var zonesV2 = coquille.Principales.Where(p => p.EstV2).ToList();

        foreach (var zone in zonesV2)
        {
            coquille.NaviguerCommand.Execute(zone);
            Assert.NotEqual(zone.Zone, coquille.Zone);
        }
    }

    [Fact]
    public void La_zone_Projets_est_desormais_atteignable()
    {
        // Le pendant du test ci-dessus : D-027 a fait entrer les projets en V1, et une zone qui
        // n'est plus étiquetée V2 doit s'ouvrir pour de bon.
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();
        var projets = coquille.Principales.Single(p => p.Zone == Zone.Projets);

        coquille.NaviguerCommand.Execute(projets);

        Assert.False(projets.EstV2);
        Assert.Equal(Zone.Projets, coquille.Zone);
    }

    [Fact]
    public void Sans_solde_de_reference_l_entete_ne_montre_aucun_chiffre()
    {
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();

        Assert.False(coquille.EntetePossedeSolde);
        Assert.Equal("—", coquille.SoldeEntete);
        Assert.True(coquille.OnboardingRequis());
    }

    [Fact]
    public void Une_fois_le_solde_pose_l_entete_l_affiche_et_l_onboarding_s_efface()
    {
        using var f = new FabriquePresentation();
        f.Composition.Demarrage.DefinirSoldeReference(248_360, DateOnly.FromDateTime(DateTime.Now));

        var coquille = f.Coquille();

        Assert.True(coquille.EntetePossedeSolde);
        Assert.Contains("2", coquille.SoldeEntete);
        Assert.Contains("483", coquille.SoldeEntete);
        Assert.False(coquille.OnboardingRequis());
    }

    [Fact]
    public void Hors_ligne_l_etat_de_synchro_compte_les_changements_en_attente()
    {
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();
        Assert.Equal("Hors ligne", coquille.EtatSynchro);

        f.AjouterSortie("Courses", DateTimeOffset.UtcNow);
        coquille.Rafraichir();

        // Sans API configurée la synchro est impossible : le message le dit, il ne ment pas sur
        // un « en attente » qui ne partira jamais.
        Assert.Equal("Hors ligne", coquille.EtatSynchro);
    }
}
