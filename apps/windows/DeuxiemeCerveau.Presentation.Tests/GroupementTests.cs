using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Groupement pliable par catégorie dans Finances (I-004, dans le périmètre V1). C'est de la mise
/// en forme sur des données déjà là : aucun champ, aucune entité, aucune règle de synchro en plus.
/// </summary>
public class GroupementTests
{
    private static DateTimeOffset CeMoisCi(int jour) =>
        new(new DateTime(DateTime.Now.Year, DateTime.Now.Month, jour, 12, 0, 0), TimeSpan.Zero);

    private static VueModeleFinances ParCategorie(FabriquePresentation f)
    {
        var modele = new VueModeleFinances(f.Composition);
        modele.FiltrerCommand.Execute(FiltreFinances.ParCategorie);
        return modele;
    }

    [Fact]
    public void Les_mouvements_se_rangent_sous_leur_calendrier()
    {
        using var f = new FabriquePresentation();
        var maison = f.AjouterCategorie("Maison");
        f.AjouterSortie("Loyer", CeMoisCi(5), 84_000, null, maison);
        f.AjouterSortie("Électricité", CeMoisCi(12), 9_000, null, maison);

        var groupe = Assert.Single(ParCategorie(f).Groupes);

        Assert.Equal("Maison", groupe.Nom);
        Assert.Equal(2, groupe.Lignes.Count);
        Assert.Equal("2 mouvements", groupe.Decompte);
    }

    [Fact]
    public void Ce_qui_n_a_pas_de_calendrier_tombe_dans_Sans_categorie()
    {
        // Un groupe de plein droit, pas un oubli : c'est souvent le plus gros, et le voir est ce
        // qui donne envie de ranger.
        using var f = new FabriquePresentation();
        f.AjouterSortie("Achat divers", CeMoisCi(8), 3_000);

        var groupe = Assert.Single(ParCategorie(f).Groupes);

        Assert.Equal("Sans catégorie", groupe.Nom);
    }

    [Fact]
    public void Le_sous_total_d_un_groupe_est_signe()
    {
        using var f = new FabriquePresentation();
        var vie = f.AjouterCategorie("Vie courante");
        f.AjouterSortie("Courses", CeMoisCi(3), 12_000, null, vie);

        var groupe = Assert.Single(ParCategorie(f).Groupes);

        Assert.Contains("120", groupe.Total);
        Assert.Contains("−", groupe.Total);      // signe moins typographique, pas un tiret
    }

    [Fact]
    public void Un_calendrier_sans_mouvement_ce_mois_ci_n_affiche_pas_de_groupe_vide()
    {
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Vacances");
        var maison = f.AjouterCategorie("Maison");
        f.AjouterSortie("Loyer", CeMoisCi(5), 84_000, null, maison);

        var groupe = Assert.Single(ParCategorie(f).Groupes);

        Assert.Equal("Maison", groupe.Nom);
    }

    [Fact]
    public void Un_element_a_plusieurs_calendriers_apparait_sous_chacun()
    {
        // Assumé : les sous-totaux ne s'additionnent alors pas au total du mois. Ce sont des
        // étiquettes de liste, pas des projections (règle 9).
        using var f = new FabriquePresentation();
        var maison = f.AjouterCategorie("Maison");
        var urgent = f.AjouterCategorie("Urgent");
        f.AjouterSortie("Plombier", CeMoisCi(9), 25_000, null, maison, urgent);

        var groupes = ParCategorie(f).Groupes;

        Assert.Equal(2, groupes.Count);
        Assert.All(groupes, g => Assert.Single(g.Lignes));
    }

    [Fact]
    public void Un_groupe_replie_le_reste_apres_un_changement_de_mois()
    {
        // Le même piège que les filtres de calendrier : les groupes sont reconstruits à chaque
        // chargement, et sans mémoire le moindre changement rouvrirait ce qu'on vient de fermer.
        using var f = new FabriquePresentation();
        var maison = f.AjouterCategorie("Maison");
        f.AjouterSortie("Loyer", CeMoisCi(5), 84_000, null, maison);

        var modele = ParCategorie(f);
        var groupe = Assert.Single(modele.Groupes);
        Assert.True(groupe.Deplie);

        groupe.BasculerCommand.Execute(null);
        Assert.False(groupe.Deplie);

        modele.MoisSuivantCommand.Execute(null);
        modele.MoisPrecedentCommand.Execute(null);

        Assert.False(Assert.Single(modele.Groupes).Deplie);
    }

    [Fact]
    public void La_vue_par_categorie_remplace_les_deux_listes_et_leur_etat_vide()
    {
        using var f = new FabriquePresentation();
        var modele = ParCategorie(f);

        Assert.True(modele.MontrerGroupes);
        Assert.False(modele.MontrerEntrees);
        Assert.False(modele.MontrerSorties);

        // Un mois sans mouvement afficherait sinon deux messages de vide l'un sous l'autre.
        Assert.True(modele.ListeVide);
        Assert.False(modele.MontrerVide);
        Assert.True(modele.AucunGroupe);
    }
}
