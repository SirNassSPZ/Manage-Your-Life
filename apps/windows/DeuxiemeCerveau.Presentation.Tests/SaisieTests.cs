using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Saisie typée (§13, V1) : facture, paiement, revenu, rendez-vous, envie.
/// <para>
/// Écrite tardivement, et ce n'est pas un détail : le bouton « Ajouter » de la vue Finances
/// n'était relié à rien. L'onboarding et les notes étaient les SEULS chemins de création dans
/// toute l'application.
/// </para>
/// </summary>
public class SaisieTests
{
    private static VueModeleSaisie Ouvrir(FabriquePresentation f)
    {
        var modele = new VueModeleSaisie(f.Composition);
        modele.Ouvrir();                 // tous les types, comme depuis l'accueil
        return modele;
    }

    private static IReadOnlyList<Element> Enregistres(FabriquePresentation f) =>
        f.Composition.Acces.Lire(() => f.Composition.Lecture.Actifs());

    [Fact]
    public void Une_facture_saisie_arrive_dans_la_base_locale()
    {
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Type = TypeElement.Facture;
        modele.Titre = "Loyer";
        modele.Montant = "840";

        modele.EnregistrerCommand.Execute(null);

        Assert.Null(modele.Erreur);
        Assert.False(modele.Ouvert);
        var element = Assert.Single(Enregistres(f));
        Assert.Equal("Loyer", element.Titre);
        Assert.Equal(84_000, element.MontantCentimes);
        Assert.Equal(Sens.Sortie, element.Sens);
        Assert.Equal(StatutElement.AVenir, element.Statut);
    }

    [Fact]
    public void Un_revenu_prend_le_sens_entree_et_le_statut_attendu()
    {
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Type = TypeElement.Revenu;
        modele.Titre = "Salaire";
        modele.Montant = "2380,50";

        modele.EnregistrerCommand.Execute(null);

        var element = Assert.Single(Enregistres(f));
        Assert.Equal(238_050, element.MontantCentimes);
        Assert.Equal(Sens.Entree, element.Sens);
        Assert.Equal(StatutElement.Attendu, element.Statut);
    }

    [Fact]
    public void Un_rendez_vous_ne_porte_ni_montant_ni_sens()
    {
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Type = TypeElement.Rendezvous;
        modele.Titre = "Dentiste";

        Assert.False(modele.DemandeMontant);
        modele.EnregistrerCommand.Execute(null);

        var element = Assert.Single(Enregistres(f));
        Assert.Null(element.MontantCentimes);
        Assert.Null(element.Sens);
        Assert.Equal(StatutElement.Planifie, element.Statut);
    }

    [Fact]
    public void Une_envie_porte_un_prix_facultatif_et_aucune_date()
    {
        // §3.1 depuis D-027 : le prix est estimé et facultatif ; l'envie n'a pas d'échéance.
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Type = TypeElement.Envie;
        modele.Titre = "Casque audio";
        modele.Montant = "180";

        Assert.True(modele.DemandeMontant);
        Assert.False(modele.DemandeDate);
        Assert.False(modele.MontantObligatoire);

        modele.EnregistrerCommand.Execute(null);

        var element = Assert.Single(Enregistres(f));
        Assert.Equal(18_000, element.MontantCentimes);
        Assert.Null(element.Sens);
        Assert.Null(element.DateDebut);
        Assert.Equal(StatutElement.Idee, element.Statut);
    }

    [Fact]
    public void Une_envie_sans_prix_passe_aussi()
    {
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Type = TypeElement.Envie;
        modele.Titre = "Un truc, un jour";

        modele.EnregistrerCommand.Execute(null);

        Assert.Null(modele.Erreur);
        Assert.Null(Assert.Single(Enregistres(f)).MontantCentimes);
    }

    [Fact]
    public void Un_montant_obligatoire_manquant_est_dit_avant_d_appeler_le_coeur()
    {
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Type = TypeElement.Facture;
        modele.Titre = "Loyer";

        modele.EnregistrerCommand.Execute(null);

        Assert.NotNull(modele.Erreur);
        Assert.True(modele.Ouvert);          // le formulaire reste ouvert, la saisie n'est pas perdue
        Assert.Empty(Enregistres(f));
    }

    [Fact]
    public void Un_montant_illisible_est_refuse_sans_rien_ecrire()
    {
        // La porte d'entrée de la règle 5 : « 12,34,56 » a déjà été lu 123 456 € par le passé.
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Type = TypeElement.Facture;
        modele.Titre = "Loyer";
        modele.Montant = "12,34,56";

        modele.EnregistrerCommand.Execute(null);

        Assert.NotNull(modele.Erreur);
        Assert.Empty(Enregistres(f));
    }

    [Fact]
    public void Un_titre_vide_est_refuse()
    {
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Titre = "   ";
        modele.Montant = "10";

        modele.EnregistrerCommand.Execute(null);

        Assert.NotNull(modele.Erreur);
        Assert.Empty(Enregistres(f));
    }

    [Fact]
    public void Les_categories_cochees_sont_posees_sur_l_element()
    {
        using var f = new FabriquePresentation();
        var maison = f.AjouterCategorie("Maison");
        var modele = Ouvrir(f);
        modele.Type = TypeElement.Facture;
        modele.Titre = "Loyer";
        modele.Montant = "840";
        modele.BasculerCategorieCommand.Execute(modele.Categories.Single(c => c.Id == maison));

        modele.EnregistrerCommand.Execute(null);

        Assert.Contains(maison, Assert.Single(Enregistres(f)).Categories);
    }

    [Theory]
    [InlineData(Periodicite.Aucune, null)]
    [InlineData(Periodicite.Mensuel, "FREQ=MONTHLY")]
    [InlineData(Periodicite.Hebdomadaire, "FREQ=WEEKLY")]
    [InlineData(Periodicite.Annuel, "FREQ=YEARLY")]
    public void La_periodicite_devient_une_RRULE_jamais_un_format_maison(Periodicite choix, string? attendu)
    {
        // Règle 6 : RRULE (RFC 5545), et rien d'autre.
        Assert.Equal(attendu, VueModeleSaisie.Rrule(choix));
    }

    [Fact]
    public void Une_facture_mensuelle_se_developpe_au_calendrier()
    {
        // Bout en bout : la saisie pose la RRULE, le cœur la développe, le calendrier l'affiche.
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Type = TypeElement.Facture;
        modele.Titre = "Loyer";
        modele.Montant = "840";
        modele.Date = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 5);
        modele.Periodicite = Periodicite.Mensuel;

        modele.EnregistrerCommand.Execute(null);

        var debut = new DateTimeOffset(new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1), TimeSpan.Zero);
        var occurrences = f.Composition.Acces.Lire(
            () => f.Composition.Calendrier.Occurrences(debut, debut.AddMonths(3)));

        Assert.True(occurrences.Count >= 3);
        Assert.All(occurrences, o => Assert.Equal("Loyer", o.Titre));
    }

    [Fact]
    public void Le_type_choisi_est_le_seul_actif()
    {
        // Sans état actif sur les boutons, les cinq types sont indiscernables à l'écran et
        // l'utilisateur ne voit pas ce qu'il est en train de créer.
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);

        Assert.Equal(TypeElement.Facture, Assert.Single(modele.Types, t => t.Actif).Type);

        modele.ChoisirTypeCommand.Execute(modele.Types.Single(t => t.Type == TypeElement.Envie));

        Assert.Equal(TypeElement.Envie, modele.Type);
        Assert.Equal(TypeElement.Envie, Assert.Single(modele.Types, t => t.Actif).Type);
    }

    [Fact]
    public void Fermer_le_formulaire_oublie_la_saisie_en_cours()
    {
        using var f = new FabriquePresentation();
        var modele = Ouvrir(f);
        modele.Titre = "Brouillon";
        modele.Montant = "42";

        modele.FermerCommand.Execute(null);

        Assert.False(modele.Ouvert);
        Assert.Equal("", modele.Titre);
        Assert.Equal("", modele.Montant);
    }
}

/// <summary>
/// Les types offerts dépendent de l'endroit d'où l'on ouvre le formulaire (§5.1, §5.4) : Finances
/// ne plane pas de rendez-vous, et le Calendrier ne saisit pas d'argent.
/// </summary>
public class TypesOffertsTests
{
    [Fact]
    public void Depuis_Finances_aucun_rendez_vous_n_est_proposable()
    {
        using var f = new FabriquePresentation();
        var modele = new VueModeleSaisie(f.Composition);

        modele.OuvrirFinancesCommand.Execute(null);

        Assert.DoesNotContain(modele.Types, t => t.Type == TypeElement.Rendezvous);
        Assert.Contains(modele.Types, t => t.Type == TypeElement.Facture);
        Assert.Contains(modele.Types, t => t.Type == TypeElement.Envie);
        // Le type actif doit être offert : sinon le formulaire s'ouvre sur un bouton absent.
        Assert.Contains(modele.Types, t => t.Type == modele.Type);
    }

    [Fact]
    public void Depuis_le_Calendrier_seul_le_rendez_vous_est_proposable()
    {
        using var f = new FabriquePresentation();
        var modele = new VueModeleSaisie(f.Composition);

        modele.OuvrirCalendrierCommand.Execute(null);

        Assert.Equal(TypeElement.Rendezvous, Assert.Single(modele.Types).Type);
        Assert.Equal(TypeElement.Rendezvous, modele.Type);
        Assert.False(modele.DemandeMontant);
    }

    [Fact]
    public void Rouvrir_ailleurs_remplace_la_liste_au_lieu_de_l_empiler()
    {
        using var f = new FabriquePresentation();
        var modele = new VueModeleSaisie(f.Composition);

        modele.OuvrirFinancesCommand.Execute(null);
        modele.OuvrirCalendrierCommand.Execute(null);
        modele.OuvrirFinancesCommand.Execute(null);

        Assert.Equal(4, modele.Types.Count);
        Assert.Single(modele.Types, t => t.Actif);
    }
}
