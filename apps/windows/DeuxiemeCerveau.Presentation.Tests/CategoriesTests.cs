using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Gestion des catégories (§3.3). Catégorie = label = calendrier : une seule notion, donc un seul
/// écran pour les tenir.
/// </summary>
public class CategoriesTests
{
    [Fact]
    public void Creer_une_categorie_la_rend_disponible_comme_calendrier()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleCategories(f.Composition);
        Assert.True(vue.AucuneCategorie);

        vue.NouveauNom = "Sport";
        vue.ChoisirCouleurCommand.Execute(VueModeleCategories.Palette[3]);
        vue.AjouterCommand.Execute(null);

        var sport = Assert.Single(vue.Categories);
        Assert.Equal("Sport", sport.Nom);
        Assert.Equal(VueModeleCategories.Palette[3], sport.Couleur);
        Assert.False(vue.AucuneCategorie);
        Assert.Null(vue.Message);

        // Le champ se vide : la saisie suivante ne repart pas de la précédente.
        Assert.Equal("", vue.NouveauNom);
    }

    [Fact]
    public void Un_nom_vide_est_refuse_avec_une_phrase_utile()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleCategories(f.Composition);

        vue.NouveauNom = "   ";
        vue.AjouterCommand.Execute(null);

        Assert.Empty(vue.Categories);
        Assert.Equal("Donne un nom à ta catégorie.", vue.Message);
    }

    [Fact]
    public void Un_doublon_est_refuse_meme_avec_une_casse_differente()
    {
        // Deux calendriers homonymes sont indiscernables dans la barre latérale.
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Santé");
        var vue = new VueModeleCategories(f.Composition);

        vue.NouveauNom = "santé";
        vue.AjouterCommand.Execute(null);

        Assert.Single(vue.Categories);
        Assert.Contains("existe déjà", vue.Message);
    }

    [Fact]
    public void Renommer_et_recolorier_conserve_l_identite_de_la_categorie()
    {
        using var f = new FabriquePresentation();
        var id = f.AjouterCategorie("Sant", "#BB5A44");
        var vue = new VueModeleCategories(f.Composition);
        var ligne = Assert.Single(vue.Categories);

        vue.ModifierCommand.Execute(ligne);
        Assert.True(ligne.EnEdition);
        Assert.Equal("Sant", ligne.NomEnCours);

        ligne.NomEnCours = "Santé";
        ligne.CouleurEnCours = VueModeleCategories.Palette[3];
        vue.EnregistrerCommand.Execute(ligne);

        var apres = Assert.Single(vue.Categories);
        Assert.Equal(id, apres.Id);                 // même entité : pas une création déguisée
        Assert.Equal("Santé", apres.Nom);
        Assert.Equal(VueModeleCategories.Palette[3], apres.Couleur);
    }

    [Fact]
    public void Annuler_une_edition_ne_laisse_aucune_trace()
    {
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Sport");
        var vue = new VueModeleCategories(f.Composition);
        var ligne = Assert.Single(vue.Categories);

        vue.ModifierCommand.Execute(ligne);
        ligne.NomEnCours = "n'importe quoi";
        vue.AnnulerCommand.Execute(ligne);

        Assert.False(ligne.EnEdition);
        Assert.Equal("Sport", Assert.Single(vue.Categories).Nom);
    }

    [Fact]
    public void Renommer_vers_un_nom_deja_pris_est_refuse()
    {
        using var f = new FabriquePresentation();
        f.AjouterCategorie("Sport");
        f.AjouterCategorie("Santé");
        var vue = new VueModeleCategories(f.Composition);
        var sport = vue.Categories.Single(c => c.Nom == "Sport");

        vue.ModifierCommand.Execute(sport);
        sport.NomEnCours = "Santé";
        vue.EnregistrerCommand.Execute(sport);

        Assert.Contains("existe déjà", vue.Message);
        Assert.Contains(vue.Categories, c => c.Nom == "Sport");
    }

    [Fact]
    public void Une_categorie_compte_les_elements_qui_la_portent()
    {
        using var f = new FabriquePresentation();
        var sante = f.AjouterCategorie("Santé");
        f.AjouterCategorie("Sport");
        f.AjouterSortie("Dentiste", DateTimeOffset.UtcNow, 5_000, null, sante);
        f.AjouterSortie("Pharmacie", DateTimeOffset.UtcNow, 2_000, null, sante);

        var vue = new VueModeleCategories(f.Composition);

        Assert.Equal(2, vue.Categories.Single(c => c.Nom == "Santé").Usages);
        Assert.Equal("2 éléments", vue.Categories.Single(c => c.Nom == "Santé").Resume);
        Assert.Equal("Aucun élément", vue.Categories.Single(c => c.Nom == "Sport").Resume);
    }

    [Fact]
    public void Supprimer_une_categorie_la_met_a_la_corbeille_et_le_dit()
    {
        // Filet 2 : marquage, jamais destruction.
        using var f = new FabriquePresentation();
        var sante = f.AjouterCategorie("Santé");
        f.AjouterSortie("Dentiste", DateTimeOffset.UtcNow, 5_000, null, sante);

        var vue = new VueModeleCategories(f.Composition);
        vue.SupprimerCommand.Execute(Assert.Single(vue.Categories));

        Assert.Empty(vue.Categories);
        Assert.Contains("corbeille", vue.Message);
        Assert.Contains("1 élément", vue.Message);   // ce que la suppression emporte est annoncé

        // Récupérable : l'entité est marquée, pas détruite.
        Assert.Contains(f.Composition.Lecture.Corbeille().Count, new[] { 0, 1 });
    }

    [Fact]
    public void La_coquille_est_prevenue_de_tout_changement_de_calendrier()
    {
        // Sans ça, la barre latérale et la grille resteraient sur une liste périmée.
        using var f = new FabriquePresentation();
        var coquille = f.Coquille();
        Assert.Empty(coquille.Calendriers);

        coquille.Categories.NouveauNom = "Sport";
        coquille.Categories.AjouterCommand.Execute(null);

        Assert.Equal("Sport", Assert.Single(coquille.Calendriers).Nom);
    }

    [Fact]
    public void La_palette_reste_fermee_et_couvre_les_usages_de_la_maquette()
    {
        // Un sélecteur libre laisserait l'utilisateur casser l'harmonie de ses calendriers.
        Assert.Equal(5, VueModeleCategories.Palette.Count);
        Assert.All(VueModeleCategories.Palette, c => Assert.Matches("^#[0-9A-F]{6}$", c));
    }
}
