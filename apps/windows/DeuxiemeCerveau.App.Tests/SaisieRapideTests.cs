using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Saisie rapide segmentée (reco #3, D-018 #3) et suggestion de catégorie (reco #4, D-018 #2) — Étape 4f.
/// Fonctions d'aide à la saisie : aucun nouveau champ, aucune contrainte imposée.
/// </summary>
public sealed class SaisieRapideTests : IDisposable
{
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();
    private readonly ServiceSaisie _saisie;

    public SaisieRapideTests()
    {
        var id = new IdentiteAppareil(_base.Depot);
        _saisie = new ServiceSaisie(_base.Depot, id, new HorlogeFixe(FabriqueLocale.T0));
    }

    public void Dispose() => _base.Dispose();

    private static Categorie Cat(string nom) => new()
    {
        Id = Guid.NewGuid(), Nom = nom, Couleur = "#808080", Origine = OrigineCategorie.Transversale,
    };

    [Fact]
    public void Composer_une_sortie_donne_une_facture_a_venir_enregistrable()
    {
        var element = SaisieRapide.Composer(Sens.Sortie, "Café Thomas", 350, new DateTimeOffset(2026, 7, 22, 14, 0, 0, TimeSpan.Zero));

        Assert.Equal(TypeElement.Facture, element.Type);
        Assert.Equal(StatutElement.AVenir, element.Statut);
        Assert.Null(element.Recurrence);
        Assert.True(_saisie.Enregistrer(element, EntiteSynchro.Element).Reussi); // valide comme une saisie complète
    }

    [Fact]
    public void Composer_une_entree_donne_un_revenu_attendu()
    {
        var element = SaisieRapide.Composer(Sens.Entree, "Prime", 50000, new DateTimeOffset(2026, 7, 30, 7, 0, 0, TimeSpan.Zero), "FREQ=MONTHLY");

        Assert.Equal(TypeElement.Revenu, element.Type);
        Assert.Equal(StatutElement.Attendu, element.Statut);
        Assert.Equal("FREQ=MONTHLY", element.Recurrence);
        Assert.True(_saisie.Enregistrer(element, EntiteSynchro.Element).Reussi);
    }

    [Fact]
    public void Suggestion_par_mot_cle_et_par_nom_direct()
    {
        var logement = Cat("Logement");
        var cats = new List<Categorie> { logement, Cat("Charges"), Cat("Alimentation"), Cat("Transport") };

        Assert.Equal(logement.Id, SuggestionCategorie.Suggerer("Loyer juillet", cats));        // mot-clé loyer → Logement
        Assert.Equal(cats[1].Id, SuggestionCategorie.Suggerer("Facture EDF", cats));            // mot-clé edf → Charges
        Assert.Equal(cats[2].Id, SuggestionCategorie.Suggerer("Courses Carrefour", cats));      // mot-clé course → Alimentation
        Assert.Equal(cats[3].Id, SuggestionCategorie.Suggerer("Abonnement Transport", cats));   // nom direct « Transport »
    }

    [Fact]
    public void Suggestion_absente_reste_nulle_jamais_imposee()
    {
        var cats = new List<Categorie> { Cat("Logement"), Cat("Transport") };

        Assert.Null(SuggestionCategorie.Suggerer("Cadeau anniversaire", cats)); // rien ne correspond → pas de catégorie
        Assert.Null(SuggestionCategorie.Suggerer("Loyer", []));                  // aucune catégorie disponible
        Assert.Null(SuggestionCategorie.Suggerer("   ", cats));                 // libellé vide
    }
}
