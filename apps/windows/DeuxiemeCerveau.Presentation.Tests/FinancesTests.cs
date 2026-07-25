using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Vue Finances (§5.1). Le point le plus sensible est la confirmation « payé/reçu » : la V1 ne
/// coche que les Éléments <b>ponctuels</b> (D-017, option A).
/// </summary>
public class FinancesTests
{
    private static DateTimeOffset LeJour(int jour) =>
        new(new DateOnly(DateTime.Now.Year, DateTime.Now.Month, jour).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    [Fact]
    public void Les_mouvements_du_mois_se_repartissent_en_entrees_et_sorties()
    {
        using var f = new FabriquePresentation();
        f.AjouterEntree("Salaire", LeJour(1), 238_000);
        f.AjouterSortie("Loyer", LeJour(5), 84_000);
        f.AjouterSortie("Internet", LeJour(10), 2_999);

        var vue = new VueModeleFinances(f.Composition);

        Assert.Equal("Salaire", Assert.Single(vue.Entrees).Titre);
        Assert.Equal(2, vue.Sorties.Count);
        Assert.False(vue.ListeVide);
    }

    [Fact]
    public void Une_echeance_recurrente_n_est_pas_confirmable()
    {
        // D-017 : un statut unique vaudrait pour TOUTES les occurrences de la série.
        using var f = new FabriquePresentation();
        f.AjouterSortie("Loyer", LeJour(5), 84_000, "FREQ=MONTHLY");

        var vue = new VueModeleFinances(f.Composition);
        var loyer = Assert.Single(vue.Sorties);

        Assert.False(loyer.Confirmable);
        Assert.Equal("Mensuel", loyer.Recurrence);
    }

    [Fact]
    public void Une_echeance_ponctuelle_a_venir_est_confirmable()
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Courses", LeJour(12), 7_240);

        var vue = new VueModeleFinances(f.Composition);
        var courses = Assert.Single(vue.Sorties);

        Assert.True(courses.Confirmable);
        Assert.Null(courses.Recurrence);
        Assert.False(courses.Regle);
        Assert.Equal("À valider", courses.Statut);
    }

    [Fact]
    public void Confirmer_une_selection_change_le_statut_et_le_dit()
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Courses", LeJour(12), 7_240);
        f.AjouterSortie("Assurance", LeJour(20), 4_780);

        var vue = new VueModeleFinances(f.Composition);
        foreach (var ligne in vue.Sorties) vue.BasculerCommand.Execute(ligne);
        Assert.Equal(2, vue.NombreSelectionnes);
        Assert.True(vue.PeutConfirmer);

        vue.ConfirmerSelectionCommand.Execute(null);

        Assert.Equal("2 mouvements confirmés.", vue.Message);
        Assert.All(vue.Sorties, l => Assert.True(l.Regle));
        Assert.All(vue.Sorties, l => Assert.False(l.Confirmable));   // déjà réglé
        Assert.Equal(0, vue.NombreSelectionnes);
    }

    [Fact]
    public void Confirmer_un_revenu_le_passe_a_recu()
    {
        using var f = new FabriquePresentation();
        f.AjouterEntree("Prime", LeJour(8), 50_000);

        var vue = new VueModeleFinances(f.Composition);
        vue.BasculerCommand.Execute(vue.Entrees[0]);
        vue.ConfirmerSelectionCommand.Execute(null);

        Assert.Equal("Reçu", Assert.Single(vue.Entrees).Statut);
    }

    [Fact]
    public void Basculer_une_ligne_recurrente_ne_la_selectionne_pas()
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Loyer", LeJour(5), 84_000, "FREQ=MONTHLY");

        var vue = new VueModeleFinances(f.Composition);
        vue.BasculerCommand.Execute(vue.Sorties[0]);

        Assert.False(vue.Sorties[0].Selectionne);
        Assert.Equal(0, vue.NombreSelectionnes);
        Assert.False(vue.PeutConfirmer);
    }

    [Fact]
    public void Confirmer_sans_selection_ne_dit_rien_et_ne_change_rien()
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Courses", LeJour(12));

        var vue = new VueModeleFinances(f.Composition);
        vue.ConfirmerSelectionCommand.Execute(null);

        Assert.Null(vue.Message);
        Assert.False(vue.Sorties[0].Regle);
    }

    [Fact]
    public void Les_totaux_somment_les_lignes_affichees()
    {
        using var f = new FabriquePresentation();
        f.AjouterEntree("Salaire", LeJour(1), 238_000);
        f.AjouterSortie("Loyer", LeJour(5), 84_000);
        f.AjouterSortie("Internet", LeJour(10), 2_999);

        var vue = new VueModeleFinances(f.Composition);

        Assert.StartsWith("+", vue.TotalEntrees);
        Assert.Contains("2", vue.TotalEntrees);
        Assert.Contains("380", vue.TotalEntrees);
        Assert.StartsWith("−", vue.TotalSorties);
        Assert.Contains("869", vue.TotalSorties);   // 840,00 + 29,99
    }

    [Fact]
    public void Le_chiffre_de_tete_reste_inconnu_sans_serveur()
    {
        // Règle 9 : la projection est serveur. Hors ligne, on ne l'invente pas.
        using var f = new FabriquePresentation();
        f.AjouterSortie("Loyer", LeJour(5), 84_000);

        var vue = new VueModeleFinances(f.Composition);

        Assert.Equal("—", vue.Cloture);
        Assert.False(vue.ClotureConnue);
    }

    [Theory]
    [InlineData(FiltreFinances.Tout, true, true)]
    [InlineData(FiltreFinances.Entrees, true, false)]
    [InlineData(FiltreFinances.Sorties, false, true)]
    public void Les_sous_vues_sont_des_filtres_sur_le_meme_mois(
        FiltreFinances filtre, bool entrees, bool sorties)
    {
        using var f = new FabriquePresentation();
        f.AjouterEntree("Salaire", LeJour(1));
        f.AjouterSortie("Loyer", LeJour(5));

        var vue = new VueModeleFinances(f.Composition);
        vue.FiltrerCommand.Execute(filtre);

        Assert.Equal(entrees, vue.MontrerEntrees);
        Assert.Equal(sorties, vue.MontrerSorties);

        // Le filtre masque, il ne recharge pas : les deux listes restent peuplées.
        Assert.Single(vue.Entrees);
        Assert.Single(vue.Sorties);
    }

    [Fact]
    public void Un_mois_sans_mouvement_le_dit()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleFinances(f.Composition);

        Assert.True(vue.ListeVide);
        Assert.Empty(vue.Entrees);
        Assert.Empty(vue.Sorties);
    }

    [Fact]
    public void Changer_de_mois_ne_montre_que_ce_mois_la()
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Courses", LeJour(12));

        var vue = new VueModeleFinances(f.Composition);
        Assert.Single(vue.Sorties);

        vue.MoisSuivantCommand.Execute(null);
        Assert.True(vue.ListeVide);

        vue.RevenirAujourdhuiCommand.Execute(null);
        Assert.Single(vue.Sorties);
        Assert.False(vue.HorsMoisCourant);
    }

    [Fact]
    public void Les_envies_sont_listees_avec_leur_statut()
    {
        using var f = new FabriquePresentation();
        f.AjouterEnvie("Casque audio");
        f.AjouterEnvie("Vélo", StatutElement.Planifiee);

        var vue = new VueModeleFinances(f.Composition);

        Assert.Equal(2, vue.Envies.Count);
        Assert.False(vue.AucuneEnvie);
        Assert.Contains(vue.Envies, e => e.Titre == "Vélo" && e.Statut == "Planifiée");
    }

    [Fact]
    public void Une_envie_ne_peut_pas_porter_de_montant_en_V1()
    {
        // §3.1 : l'argent est réservé aux types facture / paiement / revenu, et le cœur le fait
        // respecter. La maquette montre pourtant des prix sur les envies — contradiction connue,
        // à trancher dans la spec. Ce test fige le comportement RÉEL pour qu'un changement de
        // spec ne passe pas inaperçu.
        using var f = new FabriquePresentation();
        var envie = new Element
        {
            Type = TypeElement.Envie,
            Titre = "Casque audio",
            MontantCentimes = 18_000,
            Devise = "EUR",
            Sens = Sens.Sortie,
            Statut = StatutElement.Idee,
        };

        var resultat = f.Composition.Saisie.Enregistrer(envie, Core.Synchro.EntiteSynchro.Element);

        Assert.False(resultat.Reussi);
        Assert.Contains(resultat.Erreurs, e => e.Code == "montant_interdit");
    }

    [Fact]
    public void Une_envie_n_apparait_jamais_dans_les_mouvements_du_mois()
    {
        // Une envie n'est pas une dépense : elle ne pèse sur aucun total tant qu'on ne l'achète pas.
        using var f = new FabriquePresentation();
        f.AjouterEnvie("Casque audio");

        var vue = new VueModeleFinances(f.Composition);

        Assert.True(vue.ListeVide);
        Assert.Single(vue.Envies);
    }

    [Theory]
    [InlineData("FREQ=MONTHLY", "Mensuel")]
    [InlineData("FREQ=WEEKLY", "Hebdo")]
    [InlineData("FREQ=YEARLY;BYMONTH=6", "Annuel")]
    public void L_etiquette_de_recurrence_reste_lisible(string rrule, string attendu)
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Charge", LeJour(3), 1_000, rrule);

        var vue = new VueModeleFinances(f.Composition);

        // Une série hebdomadaire produit plusieurs occurrences dans le mois : c'est l'étiquette
        // qui est testée ici, pas leur nombre.
        Assert.NotEmpty(vue.Sorties);
        Assert.All(vue.Sorties, l => Assert.Equal(attendu, l.Recurrence));
    }
}
