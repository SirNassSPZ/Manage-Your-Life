using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>Vues de lecture (§5.4, §5.5, §5.6) et calendrier (RRULE développées pour l'affichage) — Étape 4e.</summary>
public sealed class LectureTests : IDisposable
{
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();
    private readonly ServiceSaisie _saisie;
    private readonly ServiceLecture _lecture;
    private readonly ServiceCalendrier _calendrier;

    public LectureTests()
    {
        var id = new IdentiteAppareil(_base.Depot);
        _saisie = new ServiceSaisie(_base.Depot, id, new HorlogeFixe(FabriqueLocale.T0));
        _lecture = new ServiceLecture(_base.Depot);
        _calendrier = new ServiceCalendrier(_base.Depot);
    }

    public void Dispose() => _base.Dispose();

    [Fact]
    public void Actifs_exclut_la_corbeille_et_filtre_par_type()
    {
        _saisie.Enregistrer(FabriqueLocale.NouvelleFacture(titre: "loyer"), EntiteSynchro.Element);
        _saisie.Enregistrer(
            new Element { Type = TypeElement.Note, Titre = "brouillon", Description = "idées", Statut = StatutElement.Active },
            EntiteSynchro.Element);
        var aJeter = FabriqueLocale.NouvelleFacture(titre: "annulé");
        _saisie.Enregistrer(aJeter, EntiteSynchro.Element);
        _saisie.Supprimer(EntiteSynchro.Element, aJeter.Id);

        Assert.Equal(2, _lecture.Actifs().Count);                     // les deux vivants, pas le supprimé
        Assert.Single(_lecture.Actifs(type: TypeElement.Facture));    // seulement la facture vivante
        Assert.Single(_lecture.Notes());                              // la note libre (§5.5)
        Assert.Single(_lecture.Corbeille());                          // l'élément à la corbeille (§5.6)
    }

    [Fact]
    public void Un_element_mensuel_apparait_chaque_mois_de_la_fenetre()
    {
        var loyer = FabriqueLocale.NouvelleFacture(titre: "loyer", recurrence: "FREQ=MONTHLY"); // le 5 de chaque mois
        _saisie.Enregistrer(loyer, EntiteSynchro.Element);

        var occ = _calendrier.Occurrences(
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero));

        Assert.Equal(3, occ.Count(o => o.ElementId == loyer.Id)); // juillet, août, septembre
    }

    [Fact]
    public void Un_element_ponctuel_hors_fenetre_n_apparait_pas()
    {
        _saisie.Enregistrer(FabriqueLocale.NouvelleFacture(titre: "ponctuel"), EntiteSynchro.Element); // 5 juillet, sans récurrence

        var occ = _calendrier.Occurrences(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(occ);
    }

    [Fact]
    public void Le_filtre_de_categorie_masque_les_elements_hors_selection()
    {
        var categorieId = Guid.NewGuid();
        var avec = FabriqueLocale.NouvelleFacture(titre: "catégorisé");
        avec.Categories.Add(categorieId);
        _saisie.Enregistrer(avec, EntiteSynchro.Element);
        _saisie.Enregistrer(FabriqueLocale.NouvelleFacture(titre: "sans catégorie"), EntiteSynchro.Element);

        var fenetreDebut = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var fenetreFin = new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

        // Sans filtre : les deux ; avec un filtre ne contenant pas la catégorie : seul le sans-catégorie reste.
        Assert.Equal(2, _calendrier.Occurrences(fenetreDebut, fenetreFin).Count);
        var visibles = _calendrier.Occurrences(fenetreDebut, fenetreFin, new HashSet<Guid> { Guid.NewGuid() });
        Assert.Single(visibles);
        Assert.Equal("sans catégorie", visibles[0].Titre);
    }
}
