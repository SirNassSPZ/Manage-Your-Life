namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Notifications locales (§4, D-018 #4). La décision « quoi notifier, quand » est testée ici ;
/// le côté Windows ne fait que remettre le toast.
/// </summary>
public class RappelsTests
{
    private static DateTimeOffset Midi(DateOnly jour) =>
        new(jour.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    [Fact]
    public void Une_echeance_de_demain_est_rappelee_la_veille()
    {
        // La veille, pas le jour même : un rappel qui arrive le matin d'un prélèvement est inutile.
        using var f = new FabriquePresentation();
        var demain = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        f.AjouterSortie("Loyer", Midi(demain), 84_000);

        var rappels = PlanificateurRappels.Echeances(f.Composition, DateTimeOffset.Now);

        var rappel = Assert.Single(rappels);
        Assert.Equal("Échéance demain", rappel.Titre);
        Assert.Contains("Loyer", rappel.Corps);
        Assert.Contains("840", rappel.Corps);
    }

    [Fact]
    public void Ce_qui_tombe_aujourd_hui_ou_apres_demain_ne_declenche_rien()
    {
        using var f = new FabriquePresentation();
        var aujourdhui = DateOnly.FromDateTime(DateTime.Now);
        f.AjouterSortie("Aujourd'hui", Midi(aujourdhui));
        f.AjouterSortie("Dans trois jours", Midi(aujourdhui.AddDays(3)));

        Assert.Empty(PlanificateurRappels.Echeances(f.Composition, DateTimeOffset.Now));
    }

    [Fact]
    public void Un_revenu_et_un_rendez_vous_ont_leur_propre_formulation()
    {
        using var f = new FabriquePresentation();
        var demain = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        f.AjouterEntree("Salaire", Midi(demain), 238_000);

        var rappel = Assert.Single(PlanificateurRappels.Echeances(f.Composition, DateTimeOffset.Now));

        Assert.Equal("Rentrée attendue demain", rappel.Titre);
        Assert.StartsWith("+", rappel.Corps.Split('—')[1].Trim());
    }

    [Fact]
    public void La_cle_porte_le_jour_pour_qu_un_rappel_ne_sorte_qu_une_fois()
    {
        // Le mode --rappels peut être déclenché plusieurs fois dans la journée.
        using var f = new FabriquePresentation();
        var demain = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        f.AjouterSortie("Loyer", Midi(demain));

        var premier = PlanificateurRappels.Echeances(f.Composition, DateTimeOffset.Now);
        var second = PlanificateurRappels.Echeances(f.Composition, DateTimeOffset.Now.AddHours(3));

        Assert.Equal(premier[0].Cle, second[0].Cle);
        Assert.Contains(demain.ToString("yyyy-MM-dd"), premier[0].Cle);
    }

    [Fact]
    public void Le_digest_resume_la_semaine_en_une_phrase()
    {
        using var f = new FabriquePresentation();
        var aujourdhui = DateOnly.FromDateTime(DateTime.Now);
        f.AjouterEntree("Salaire", Midi(aujourdhui.AddDays(1)), 238_000);
        f.AjouterSortie("Loyer", Midi(aujourdhui.AddDays(2)), 84_000);

        var digest = PlanificateurRappels.Digest(f.Composition, DateTimeOffset.Now);

        Assert.NotNull(digest);
        Assert.Equal("Le point de la semaine", digest!.Titre);
        Assert.Contains("2 mouvements", digest.Corps);
        // Le groupement des milliers utilise une espace insécable fine : on vise les chiffres.
        Assert.Contains("380,00", digest.Corps);
        Assert.Contains("840,00", digest.Corps);
        Assert.Contains("+", digest.Corps);
        Assert.Contains("−", digest.Corps);
    }

    [Fact]
    public void Une_semaine_vide_ne_produit_aucun_digest()
    {
        // Une notification qui ne dit rien apprend à ignorer les suivantes.
        using var f = new FabriquePresentation();

        Assert.Null(PlanificateurRappels.Digest(f.Composition, DateTimeOffset.Now));
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, true)]
    [InlineData(DayOfWeek.Monday, false)]
    [InlineData(DayOfWeek.Saturday, false)]
    public void Le_digest_est_un_rendez_vous_du_dimanche(DayOfWeek jour, bool attendu)
    {
        var date = new DateTime(2026, 7, 26);            // un dimanche
        var vise = date.AddDays(((int)jour - (int)DayOfWeek.Sunday + 7) % 7);

        Assert.Equal(attendu, PlanificateurRappels.EstJourDuDigest(new DateTimeOffset(vise)));
    }
}
