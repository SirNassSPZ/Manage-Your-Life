using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Onboarding « 3 gestes » et modèles de départ (feuille de route Palier 1 rang 2 / reco #1, D-018 #1) —
/// Étape 4f. Tout repose sur la saisie ordinaire (§6) : les modèles ne produisent que des Éléments
/// standard (§3.1), rien de nouveau dans le modèle.
/// </summary>
public sealed class DemarrageTests : IDisposable
{
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();
    private readonly ServiceSaisie _saisie;
    private readonly ServiceDemarrage _demarrage;
    private readonly ServiceLecture _lecture;
    private readonly ServiceAujourdhui _accueil;

    public DemarrageTests()
    {
        var id = new IdentiteAppareil(_base.Depot);
        _saisie = new ServiceSaisie(_base.Depot, id, new HorlogeFixe(FabriqueLocale.T0));
        _demarrage = new ServiceDemarrage(_base.Depot, _saisie);
        _lecture = new ServiceLecture(_base.Depot);
        _accueil = new ServiceAujourdhui(_base.Depot, new ServiceCalendrier(_base.Depot));
    }

    public void Dispose() => _base.Dispose();

    [Fact]
    public void Onboarding_requis_tant_que_le_solde_de_reference_n_est_pas_pose()
    {
        Assert.True(_demarrage.OnboardingRequis());

        var r = _demarrage.DefinirSoldeReference(248000, new DateOnly(2026, 7, 24));

        Assert.True(r.Reussi);
        Assert.False(_demarrage.OnboardingRequis());
        // Geste 1 visible immédiatement par l'accueil (même donnée, §3.4).
        Assert.Equal(248000, _accueil.SoldeDeReference()!.Centimes);
    }

    [Fact]
    public void Le_catalogue_couvre_un_revenu_et_des_charges()
    {
        var modeles = _demarrage.Modeles;

        var salaire = Assert.Single(modeles, m => m.Cle == "salaire");
        Assert.Equal(TypeElement.Revenu, salaire.Type);
        Assert.Equal(Sens.Entree, salaire.Sens);

        var loyer = Assert.Single(modeles, m => m.Cle == "loyer");
        Assert.Equal(Sens.Sortie, loyer.Sens);
        Assert.All(modeles, m => Assert.False(string.IsNullOrWhiteSpace(m.Recurrence))); // tous récurrents
    }

    [Fact]
    public void Un_modele_compose_un_Element_valide_enregistrable()
    {
        var loyer = _demarrage.Modeles.Single(m => m.Cle == "loyer");

        var element = loyer.Composer(80000, new DateTimeOffset(2026, 7, 5, 7, 0, 0, TimeSpan.Zero));
        var r = _saisie.Enregistrer(element, EntiteSynchro.Element);

        Assert.True(r.Reussi); // passe les validations du cœur (§3.1) comme une saisie manuelle
        Assert.Equal(StatutElement.AVenir, element.Statut);
        var enregistre = Assert.Single(_lecture.Actifs(type: TypeElement.Facture));
        Assert.Equal("Loyer", enregistre.Titre);
        Assert.Equal(80000, enregistre.MontantCentimes);
    }

    [Fact]
    public void Un_modele_de_revenu_est_attendu_par_defaut()
    {
        var salaire = _demarrage.Modeles.Single(m => m.Cle == "salaire");

        var element = salaire.Composer(240000, new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero));

        Assert.Equal(Sens.Entree, element.Sens);
        Assert.Equal(StatutElement.Attendu, element.Statut); // revenu : attendu → reçu (§3.1)
        Assert.True(_saisie.Enregistrer(element, EntiteSynchro.Element).Reussi);
    }
}
