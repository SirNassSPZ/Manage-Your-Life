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

        var coquille = new VueModeleCoquille(f.Composition);
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
        var coquille = new VueModeleCoquille(f.Composition);
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
        var coquille = new VueModeleCoquille(f.Composition);
        var sante = coquille.Calendriers.Single(c => c.Nom == "Santé");

        coquille.BasculerCalendrierCommand.Execute(sante);
        coquille.BasculerCalendrierCommand.Execute(coquille.Calendriers.Single(c => c.Nom == "Santé"));

        Assert.True(coquille.Calendriers.Single(c => c.Nom == "Santé").Visible);
    }

    [Theory]
    [InlineData(Zone.Aujourdhui, true)]
    [InlineData(Zone.Finances, true)]
    [InlineData(Zone.Calendrier, false)]   // ses sous-vues SONT les calendriers, listés à part
    [InlineData(Zone.BudgetProjete, false)]
    public void L_intitule_des_sous_vues_ne_reste_jamais_seul(Zone zone, bool attendu)
    {
        using var f = new FabriquePresentation();
        var coquille = new VueModeleCoquille(f.Composition);

        coquille.Aller(zone);

        Assert.Equal(attendu, coquille.ASousVues);
        Assert.Equal(attendu, coquille.SousVues.Count > 0);
    }

    [Fact]
    public void Une_zone_V2_reste_inerte()
    {
        // Périmètre V1 verrouillé : la zone est montrée, jamais atteinte.
        using var f = new FabriquePresentation();
        var coquille = new VueModeleCoquille(f.Composition);
        var projets = coquille.Principales.Single(p => p.EstV2);

        coquille.NaviguerCommand.Execute(projets);

        Assert.NotEqual(projets.Zone, coquille.Zone);
    }

    [Fact]
    public void Sans_solde_de_reference_l_entete_ne_montre_aucun_chiffre()
    {
        using var f = new FabriquePresentation();
        var coquille = new VueModeleCoquille(f.Composition);

        Assert.False(coquille.EntetePossedeSolde);
        Assert.Equal("—", coquille.SoldeEntete);
        Assert.True(coquille.OnboardingRequis());
    }

    [Fact]
    public void Une_fois_le_solde_pose_l_entete_l_affiche_et_l_onboarding_s_efface()
    {
        using var f = new FabriquePresentation();
        f.Composition.Demarrage.DefinirSoldeReference(248_360, DateOnly.FromDateTime(DateTime.Now));

        var coquille = new VueModeleCoquille(f.Composition);

        Assert.True(coquille.EntetePossedeSolde);
        Assert.Contains("2", coquille.SoldeEntete);
        Assert.Contains("483", coquille.SoldeEntete);
        Assert.False(coquille.OnboardingRequis());
    }

    [Fact]
    public void Hors_ligne_l_etat_de_synchro_compte_les_changements_en_attente()
    {
        using var f = new FabriquePresentation();
        var coquille = new VueModeleCoquille(f.Composition);
        Assert.Equal("Hors ligne", coquille.EtatSynchro);

        f.AjouterSortie("Courses", DateTimeOffset.UtcNow);
        coquille.Rafraichir();

        // Sans API configurée la synchro est impossible : le message le dit, il ne ment pas sur
        // un « en attente » qui ne partira jamais.
        Assert.Equal("Hors ligne", coquille.EtatSynchro);
    }
}
