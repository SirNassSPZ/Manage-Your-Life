using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Grille mensuelle du calendrier (§5.4). Tout est local : aucun de ces tests ne touche le réseau.
/// </summary>
public class CalendrierTests
{
    private static VueModeleCalendrier Vue(FabriquePresentation f, Func<IReadOnlySet<Guid>?>? filtres = null)
        => new(f.Composition, filtres ?? (() => null));

    [Fact]
    public void La_grille_fait_toujours_six_semaines_pleines()
    {
        using var f = new FabriquePresentation();
        var vue = Vue(f);

        Assert.Equal(42, vue.Cases.Count);

        // Y compris en changeant de mois : une grille qui change de hauteur fait sauter la mise en
        // page à chaque flèche.
        for (var i = 0; i < 14; i++)
        {
            vue.MoisSuivantCommand.Execute(null);
            Assert.Equal(42, vue.Cases.Count);
        }
    }

    [Fact]
    public void La_grille_commence_au_lundi_de_la_semaine_du_premier()
    {
        using var f = new FabriquePresentation();
        var vue = Vue(f);

        var premierDuMois = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1);
        var decalage = ((int)premierDuMois.DayOfWeek + 6) % 7;   // WKST=MO (D-003)
        var attendu = premierDuMois.AddDays(-decalage);

        Assert.Equal(attendu.Day.ToString(), vue.Cases[0].Numero);
        Assert.Equal(DayOfWeek.Monday, attendu.DayOfWeek);
    }

    [Fact]
    public void Le_jour_courant_est_marque_une_seule_fois()
    {
        using var f = new FabriquePresentation();
        var vue = Vue(f);

        var marques = vue.Cases.Where(c => c.EstAujourdhui).ToList();
        Assert.Single(marques);
        Assert.Equal(DateTime.Now.Day.ToString(), marques[0].Numero);
        Assert.False(marques[0].HorsMois);
    }

    [Fact]
    public void Un_autre_mois_ne_marque_aucun_jour_courant()
    {
        using var f = new FabriquePresentation();
        var vue = Vue(f);
        vue.MoisSuivantCommand.Execute(null);

        Assert.DoesNotContain(vue.Cases, c => c.EstAujourdhui);
        Assert.True(vue.HorsMoisCourant);
    }

    [Fact]
    public void Revenir_aujourdhui_ramene_au_mois_courant()
    {
        using var f = new FabriquePresentation();
        var vue = Vue(f);
        vue.MoisSuivantCommand.Execute(null);
        vue.MoisSuivantCommand.Execute(null);
        vue.MoisPrecedentCommand.Execute(null);
        Assert.True(vue.HorsMoisCourant);

        vue.RevenirAujourdhuiCommand.Execute(null);

        Assert.False(vue.HorsMoisCourant);
        Assert.Contains(vue.Cases, c => c.EstAujourdhui);
    }

    [Fact]
    public void Les_jours_des_mois_voisins_sont_estompes_et_les_autres_non()
    {
        using var f = new FabriquePresentation();
        var vue = Vue(f);

        var mois = DateTime.Now.Month;
        var joursDuMois = DateTime.DaysInMonth(DateTime.Now.Year, mois);

        // Exactement les jours du mois affiché sont « dans le mois ».
        Assert.Equal(joursDuMois, vue.Cases.Count(c => !c.HorsMois));
    }

    [Fact]
    public void Une_echeance_se_pose_sur_son_jour_local()
    {
        using var f = new FabriquePresentation();
        var jour = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 15);
        // Midi local : à l'abri des deux conventions de bascule d'heure (D-002).
        f.AjouterSortie("Loyer", new DateTimeOffset(jour.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), 84_000);

        var vue = Vue(f);
        var case15 = vue.Cases.Single(c => !c.HorsMois && c.Numero == "15");

        var pastille = Assert.Single(case15.Pastilles);
        Assert.Equal("Loyer", pastille.Titre);
        Assert.Contains("840", pastille.Montant);
    }

    [Fact]
    public void Au_dela_de_trois_echeances_la_case_annonce_le_reste()
    {
        using var f = new FabriquePresentation();
        var jour = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 10);
        var instant = new DateTimeOffset(jour.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        for (var i = 1; i <= 5; i++) f.AjouterSortie($"Sortie {i}", instant);

        var vue = Vue(f);
        var case10 = vue.Cases.Single(c => !c.HorsMois && c.Numero == "10");

        Assert.Equal(3, case10.Pastilles.Count);
        Assert.Equal("+2", case10.Debordement);
    }

    [Fact]
    public void Trois_echeances_ou_moins_n_annoncent_aucun_reste()
    {
        using var f = new FabriquePresentation();
        var jour = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 10);
        var instant = new DateTimeOffset(jour.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        for (var i = 1; i <= 3; i++) f.AjouterSortie($"Sortie {i}", instant);

        var vue = Vue(f);
        var case10 = vue.Cases.Single(c => !c.HorsMois && c.Numero == "10");

        Assert.Equal(3, case10.Pastilles.Count);
        Assert.Null(case10.Debordement);
    }

    [Fact]
    public void Une_recurrence_mensuelle_apparait_sur_chaque_mois_affiche()
    {
        using var f = new FabriquePresentation();
        var premier = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 5);
        f.AjouterSortie(
            "Loyer",
            new DateTimeOffset(premier.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            84_000,
            "FREQ=MONTHLY");

        var vue = Vue(f);
        Assert.Contains(vue.Cases.Where(c => !c.HorsMois), c => c.Pastilles.Any(p => p.Titre == "Loyer"));

        vue.MoisSuivantCommand.Execute(null);
        Assert.Contains(vue.Cases.Where(c => !c.HorsMois), c => c.Pastilles.Any(p => p.Titre == "Loyer"));
    }

    [Fact]
    public void Un_calendrier_masque_retire_ses_echeances_de_la_grille()
    {
        using var f = new FabriquePresentation();
        var sante = f.AjouterCategorie("Santé", "#A97E3C");
        var travail = f.AjouterCategorie("Travail", "#4B7F8A");
        var jour = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 12);
        var instant = new DateTimeOffset(jour.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        f.AjouterSortie("Dentiste", instant, 5_000, null, sante);
        f.AjouterSortie("Déjeuner équipe", instant, 3_000, null, travail);

        // Seul « Travail » reste visible.
        var visibles = new HashSet<Guid> { travail };
        var vue = Vue(f, () => visibles);
        var case12 = vue.Cases.Single(c => !c.HorsMois && c.Numero == "12");

        Assert.Equal("Déjeuner équipe", Assert.Single(case12.Pastilles).Titre);
    }

    [Fact]
    public void La_vue_sept_jours_couvre_toujours_sept_jours_a_partir_d_aujourdhui()
    {
        using var f = new FabriquePresentation();
        var vue = Vue(f);
        vue.ChoisirModeCommand.Execute(ModeCalendrier.SeptJours);

        Assert.True(vue.EstSeptJours);
        Assert.False(vue.EstMois);
        Assert.Equal(7, vue.Semaine.Count);
        Assert.True(vue.Semaine[0].EstAujourdhui);
        Assert.DoesNotContain(vue.Semaine.Skip(1), j => j.EstAujourdhui);
    }

    [Fact]
    public void Un_jour_sans_mouvement_le_dit_plutot_que_de_disparaitre()
    {
        // L'absence de mouvement est une information : « rien de prévu » vaut mieux qu'un trou.
        using var f = new FabriquePresentation();
        var vue = Vue(f);
        vue.ChoisirModeCommand.Execute(ModeCalendrier.SeptJours);

        Assert.All(vue.Semaine, j => Assert.Equal("Rien de prévu", j.Resume));
        Assert.All(vue.Semaine, j => Assert.Empty(j.Pastilles));
    }

    [Fact]
    public void La_vue_sept_jours_montre_les_mouvements_a_venir_avec_leur_montant()
    {
        using var f = new FabriquePresentation();
        var demain = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        f.AjouterSortie("Courses", new DateTimeOffset(demain.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), 9_000);

        var vue = Vue(f);
        vue.ChoisirModeCommand.Execute(ModeCalendrier.SeptJours);

        var jourDemain = vue.Semaine[1];
        Assert.Equal("1 mouvement", jourDemain.Resume);
        var pastille = Assert.Single(jourDemain.Pastilles);
        Assert.Equal("Courses", pastille.Titre);
        Assert.Contains("90", pastille.Montant);
    }

    [Fact]
    public void Les_intitules_de_la_semaine_sont_en_capitales_comme_la_maquette()
    {
        using var f = new FabriquePresentation();
        var vue = Vue(f);
        vue.ChoisirModeCommand.Execute(ModeCalendrier.SeptJours);

        Assert.StartsWith("AUJOURD'HUI", vue.Semaine[0].Intitule);
        Assert.StartsWith("DEMAIN", vue.Semaine[1].Intitule);
    }

    [Fact]
    public void Passer_en_sept_jours_ramene_au_mois_courant()
    {
        // La semaine est glissante à partir d'aujourd'hui : rester sur un mois lointain n'aurait
        // aucun sens, et le retour au mois trouverait une grille désorientée.
        using var f = new FabriquePresentation();
        var vue = Vue(f);
        vue.MoisSuivantCommand.Execute(null);
        vue.MoisSuivantCommand.Execute(null);
        Assert.True(vue.HorsMoisCourant);

        vue.ChoisirModeCommand.Execute(ModeCalendrier.SeptJours);

        Assert.False(vue.HorsMoisCourant);
    }

    [Fact]
    public void Un_calendrier_masque_disparait_aussi_de_la_vue_sept_jours()
    {
        using var f = new FabriquePresentation();
        var sante = f.AjouterCategorie("Santé");
        var travail = f.AjouterCategorie("Travail");
        var demain = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        var instant = new DateTimeOffset(demain.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        f.AjouterSortie("Dentiste", instant, 5_000, null, sante);
        f.AjouterSortie("Déjeuner équipe", instant, 3_000, null, travail);

        var visibles = new HashSet<Guid> { travail };
        var vue = Vue(f, () => visibles);
        vue.ChoisirModeCommand.Execute(ModeCalendrier.SeptJours);

        Assert.Equal("Déjeuner équipe", Assert.Single(vue.Semaine[1].Pastilles).Titre);
    }

    [Fact]
    public void Le_mode_gestion_ne_montre_ni_grille_ni_semaine()
    {
        using var f = new FabriquePresentation();
        var vue = Vue(f);
        vue.ChoisirModeCommand.Execute(ModeCalendrier.Gestion);

        Assert.True(vue.EstGestion);
        Assert.False(vue.EstMois);
        Assert.False(vue.EstSeptJours);
    }

    [Fact]
    public void Un_element_sans_categorie_reste_visible_quels_que_soient_les_filtres()
    {
        using var f = new FabriquePresentation();
        var sante = f.AjouterCategorie("Santé");
        var jour = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 12);
        f.AjouterSortie("Courses", new DateTimeOffset(jour.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));

        var vue = Vue(f, () => new HashSet<Guid> { sante });
        var case12 = vue.Cases.Single(c => !c.HorsMois && c.Numero == "12");

        Assert.Equal("Courses", Assert.Single(case12.Pastilles).Titre);
    }
}
