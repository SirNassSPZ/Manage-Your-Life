using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Validation;
using Xunit;

namespace DeuxiemeCerveau.Core.Tests;

/// <summary>Matrice exhaustive types × statuts (§3.1, règle 8) — chaque case de la table, et rien d'autre.</summary>
public class StatutsParTypeTests
{
    public static TheoryData<TypeElement, StatutElement, bool> Matrice()
    {
        var donnees = new TheoryData<TypeElement, StatutElement, bool>();
        foreach (var type in Enum.GetValues<TypeElement>())
            foreach (var statut in Enum.GetValues<StatutElement>())
                donnees.Add(type, statut, StatutsAutorises.ParType[type].Contains(statut));
        return donnees;
    }

    [Theory]
    [MemberData(nameof(Matrice))]
    public void La_table_du_3_1_est_appliquee_case_par_case(TypeElement type, StatutElement statut, bool autorise)
    {
        var element = type switch
        {
            TypeElement.Facture or TypeElement.Paiement => Fabrique.Facture(),
            TypeElement.Revenu => Fabrique.Revenu(),
            TypeElement.Tache => Fabrique.Tache(),
            _ => Fabrique.Note(),
        };
        element.Type = type;
        element.Statut = statut;

        var erreurs = ValidateurElement.Valider(element);
        if (autorise)
            Assert.DoesNotContain(erreurs, e => e.Code == "statut_interdit");
        else
            Assert.Contains(erreurs, e => e.Code == "statut_interdit");
    }

    [Fact]
    public void La_table_est_exactement_celle_de_la_spec()
    {
        // Verrouillage : 7 types, ensembles exacts (§3.1). Toute divergence casse ce test.
        Assert.Equal(7, StatutsAutorises.ParType.Count);
        Assert.Equal([StatutElement.AVenir, StatutElement.Paye, StatutElement.Annule],
            StatutsAutorises.ParType[TypeElement.Facture].Order());
        Assert.Equal([StatutElement.AVenir, StatutElement.Paye, StatutElement.Annule],
            StatutsAutorises.ParType[TypeElement.Paiement].Order());
        Assert.Equal([StatutElement.Annule, StatutElement.Attendu, StatutElement.Recu],
            StatutsAutorises.ParType[TypeElement.Revenu].Order());
        Assert.Equal([StatutElement.Annule, StatutElement.AFaire, StatutElement.Fait, StatutElement.Reporte],
            StatutsAutorises.ParType[TypeElement.Tache].Order());
        Assert.Equal([StatutElement.Annule, StatutElement.Fait, StatutElement.Planifie],
            StatutsAutorises.ParType[TypeElement.Rendezvous].Order());
        Assert.Equal([StatutElement.Idee, StatutElement.Planifiee, StatutElement.Faite, StatutElement.Abandonnee],
            StatutsAutorises.ParType[TypeElement.Envie].Order());
        Assert.Equal([StatutElement.Active, StatutElement.Archivee],
            StatutsAutorises.ParType[TypeElement.Note].Order());
    }
}

public class ValidateurElementTests
{
    private static void AssertErreur(Element element, string code)
        => Assert.Contains(ValidateurElement.Valider(element), e => e.Code == code);

    private static void AssertValide(Element element)
        => Assert.Empty(ValidateurElement.Valider(element));

    [Fact]
    public void Element_complet_valide() => AssertValide(Fabrique.Facture());

    [Fact]
    public void Titre_obligatoire()
    {
        var e = Fabrique.Facture(titre: "   ");
        AssertErreur(e, "titre_manquant");
    }

    [Fact]
    public void Titre_limite_a_300()
    {
        AssertValide(Fabrique.Facture(titre: new string('a', 300)));
        AssertErreur(Fabrique.Facture(titre: new string('a', 301)), "titre_trop_long");
    }

    // ----- Temps -----

    [Fact]
    public void Fuseau_obligatoire_des_quune_date_est_presente()
        => AssertErreur(Fabrique.Facture(fuseau: null), "fuseau_manquant");

    [Fact]
    public void Fuseau_sans_date_rejete()
    {
        var e = Fabrique.Note();
        e.Fuseau = "Europe/Paris";
        AssertErreur(e, "fuseau_sans_date");
    }

    [Theory]
    [InlineData("Romance Standard Time")]
    [InlineData("Europe/Nulle_Part")]
    public void Fuseau_non_iana_rejete(string fuseau)
        => AssertErreur(Fabrique.Facture(fuseau: fuseau), "fuseau_invalide");

    [Fact]
    public void Date_fin_avant_date_debut_rejetee()
    {
        var e = Fabrique.Facture();
        e.DateFin = e.DateDebut!.Value.AddHours(-1);
        AssertErreur(e, "dates_incoherentes");
    }

    [Fact]
    public void Date_fin_sans_debut_rejetee()
    {
        var e = Fabrique.Note();
        e.DateFin = Fabrique.T0;
        e.Fuseau = "Europe/Paris";
        AssertErreur(e, "date_fin_sans_debut");
    }

    [Fact]
    public void Journee_entiere_exige_date()
    {
        var e = Fabrique.Note();
        e.JourneeEntiere = true;
        AssertErreur(e, "journee_sans_date");
    }

    [Fact]
    public void Date_approximative_reservee_aux_envies()
    {
        var facture = Fabrique.Facture();
        facture.DateApproximative = true;
        AssertErreur(facture, "date_approximative_reservee");

        var envie = Fabrique.Note();
        envie.Type = TypeElement.Envie;
        envie.Statut = StatutElement.Idee;
        envie.DateApproximative = true;
        AssertValide(envie);
    }

    // ----- Récurrence -----

    [Fact]
    public void Recurrence_invalide_rejetee()
        => AssertErreur(Fabrique.Facture(recurrence: "FREQ=MONTHLY;BYSETPOS=-1;BYDAY=FR"), "recurrence_invalide");

    [Fact]
    public void Recurrence_sans_date_rejetee()
    {
        var e = Fabrique.Note();
        e.Recurrence = "FREQ=DAILY";
        AssertErreur(e, "recurrence_sans_date");
    }

    [Fact]
    public void Recurrence_valide_acceptee()
        => AssertValide(Fabrique.Facture(recurrence: "FREQ=MONTHLY;BYMONTHDAY=-1"));

    // ----- Argent (règle 5) -----

    [Fact]
    public void Montant_obligatoire_sur_type_financier()
    {
        var e = Fabrique.Facture();
        e.MontantCentimes = null;
        AssertErreur(e, "montant_manquant");
    }

    [Fact]
    public void Montant_negatif_rejete()
        => AssertErreur(Fabrique.Facture(montant: -1), "montant_negatif");

    [Fact]
    public void Montant_interdit_hors_types_financiers()
    {
        var e = Fabrique.Note();
        e.MontantCentimes = 100;
        AssertErreur(e, "montant_interdit");
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("E")]
    public void Devise_format_iso_4217(string devise)
    {
        var e = Fabrique.Facture();
        e.Devise = devise;
        AssertErreur(e, "devise_invalide");
    }

    [Fact]
    public void Sens_incoherent_avec_le_type_rejete()
    {
        var facture = Fabrique.Facture();
        facture.Sens = Sens.Entree; // une facture est une sortie (§3.1)
        AssertErreur(facture, "sens_incoherent");

        var revenu = Fabrique.Revenu();
        revenu.Sens = Sens.Sortie;
        AssertErreur(revenu, "sens_incoherent");
    }

    // ----- budget_id (§3.6) -----

    [Fact]
    public void Budget_accepte_sur_les_sorties_seulement()
    {
        var facture = Fabrique.Facture();
        facture.BudgetId = Guid.NewGuid();
        AssertValide(facture);

        var revenu = Fabrique.Revenu();
        revenu.BudgetId = Guid.NewGuid();
        AssertErreur(revenu, "budget_interdit");

        var note = Fabrique.Note();
        note.BudgetId = Guid.NewGuid();
        AssertErreur(note, "budget_interdit");
    }

    // ----- Champs de tâche (D-009) -----

    [Fact]
    public void Champs_de_tache_reserves_au_type_tache()
    {
        var note = Fabrique.Note();
        note.Priorite = Priorite.Haute;
        AssertErreur(note, "champ_reserve_taches");

        note = Fabrique.Note();
        note.ScorePoints = 10;
        AssertErreur(note, "champ_reserve_taches");

        note = Fabrique.Note();
        note.OrdreManuel = 1;
        AssertErreur(note, "champ_reserve_taches");

        note = Fabrique.Note();
        note.EstObligatoire = true;
        AssertErreur(note, "champ_reserve_taches");

        var tache = Fabrique.Tache();
        tache.Priorite = Priorite.Haute;
        tache.ScorePoints = 10;
        tache.OrdreManuel = 1;
        tache.EstObligatoire = true;
        AssertValide(tache);
    }

    // ----- Rappels -----

    [Fact]
    public void Rappel_relatif_exige_minutes_sans_date()
    {
        var e = Fabrique.Facture();
        e.Rappels.Add(new Rappel { Type = TypeRappel.Relatif });
        AssertErreur(e, "rappel_invalide");

        e = Fabrique.Facture();
        e.Rappels.Add(new Rappel { Type = TypeRappel.Relatif, MinutesAvant = 30, Date = Fabrique.T0 });
        AssertErreur(e, "rappel_invalide");

        e = Fabrique.Facture();
        e.Rappels.Add(new Rappel { Type = TypeRappel.Relatif, MinutesAvant = 30 });
        AssertValide(e);
    }

    [Fact]
    public void Rappel_absolu_exige_date_sans_minutes()
    {
        var e = Fabrique.Facture();
        e.Rappels.Add(new Rappel { Type = TypeRappel.Absolu });
        AssertErreur(e, "rappel_invalide");

        e = Fabrique.Facture();
        e.Rappels.Add(new Rappel { Type = TypeRappel.Absolu, Date = Fabrique.T0 });
        AssertValide(e);
    }

    // ----- Audit / synchro -----

    [Fact]
    public void Version_commence_a_un()
    {
        var e = Fabrique.Facture(version: 0);
        AssertErreur(e, "version_invalide");
    }

    [Fact]
    public void Suppression_coherente_exigee()
    {
        var e = Fabrique.Facture();
        e.Supprime = true; // sans date_suppression
        AssertErreur(e, "suppression_incoherente");

        e = Fabrique.Facture();
        e.DateSuppression = Fabrique.T0; // sans supprime
        AssertErreur(e, "suppression_incoherente");

        e = Fabrique.Facture();
        e.Supprime = true;
        e.DateSuppression = Fabrique.T0;
        AssertValide(e);
    }

    [Fact]
    public void Date_modification_jamais_avant_creation()
    {
        var e = Fabrique.Facture();
        e.DateModification = e.DateCreation.AddSeconds(-1);
        AssertErreur(e, "audit_incoherent");
    }

    [Fact]
    public void Categories_dupliquees_rejetees()
    {
        var e = Fabrique.Facture();
        var categorie = Guid.NewGuid();
        e.Categories.AddRange([categorie, categorie]);
        AssertErreur(e, "categories_dupliquees");
    }
}

public class ValidateurEntitesTests
{
    [Fact]
    public void Categorie_valide() => Assert.Empty(ValidateurEntites.Valider(Fabrique.Categorie()));

    [Theory]
    [InlineData("3366FF")]
    [InlineData("#36F")]
    [InlineData("#GGGGGG")]
    public void Couleur_format_strict(string couleur)
    {
        var categorie = Fabrique.Categorie();
        categorie.Couleur = couleur;
        Assert.Contains(ValidateurEntites.Valider(categorie), e => e.Code == "couleur_invalide");
    }

    [Fact]
    public void Budget_plafond_negatif_rejete()
    {
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            Nom = "Courses",
            Couleur = "#112233",
            MontantPeriodeCentimes = -1,
            Periode = PeriodeBudget.Mensuel,
            Statut = StatutBudget.Actif,
            DateCreation = Fabrique.T0,
            DateModification = Fabrique.T0,
            AppareilSource = Fabrique.AppareilA,
            Version = 1,
        };
        Assert.Contains(ValidateurEntites.Valider(budget), e => e.Code == "montant_negatif");
    }

    [Fact]
    public void Piece_jointe_limite_25_mo()
    {
        var piece = new PieceJointe
        {
            Id = Guid.NewGuid(),
            ElementId = Guid.NewGuid(),
            NomFichier = "facture.pdf",
            TailleOctets = PieceJointe.TailleMaxOctets + 1,
            BlobPath = "blobs/x",
            DateCreation = Fabrique.T0,
            DateModification = Fabrique.T0,
            AppareilSource = Fabrique.AppareilA,
            Version = 1,
        };
        Assert.Contains(ValidateurEntites.Valider(piece), e => e.Code == "taille_depassee");
    }
}
