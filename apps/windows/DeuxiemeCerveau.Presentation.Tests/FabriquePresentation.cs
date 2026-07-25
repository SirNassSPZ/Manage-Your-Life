using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Monte un graphe applicatif COMPLET sur un dossier temporaire : vraie base SQLite, vraies
/// migrations du cœur, vrais services. Seuls le réseau et l'identité sont absents
/// (<see cref="FournisseurJetonAbsent"/>) — c'est-à-dire exactement l'état « hors ligne », qui est
/// le mode nominal de l'app (filet 1), pas un cas de repli.
/// </summary>
public sealed class FabriquePresentation : IDisposable
{
    private readonly string _dossier;

    public FabriquePresentation()
    {
        _dossier = Path.Combine(Path.GetTempPath(), "dc-tests-" + Guid.NewGuid().ToString("N"));
        Composition = Composition.Creer(new OptionsApp(), new FournisseurJetonAbsent(), _dossier);
    }

    public Composition Composition { get; }

    /// <summary>Enregistre une catégorie (= un calendrier, §3.3) et renvoie son identifiant.</summary>
    public Guid AjouterCategorie(string nom, string couleur = "#4A8C63")
    {
        var categorie = new Categorie { Nom = nom, Couleur = couleur, Origine = OrigineCategorie.Transversale };
        Composition.Saisie.Enregistrer(categorie, EntiteSynchro.Categorie);
        return categorie.Id;
    }

    /// <summary>Enregistre une sortie datée, éventuellement récurrente et catégorisée.</summary>
    public Guid AjouterSortie(
        string titre,
        DateTimeOffset date,
        long centimes = 1_000,
        string? recurrence = null,
        params Guid[] categories)
    {
        var element = new Element
        {
            Type = TypeElement.Facture,
            Titre = titre,
            DateDebut = date,
            Fuseau = "Europe/Paris",
            Recurrence = recurrence,
            MontantCentimes = centimes,
            Devise = "EUR",
            Sens = Sens.Sortie,
            Statut = StatutElement.AVenir,
        };
        foreach (var categorie in categories) element.Categories.Add(categorie);

        Composition.Saisie.Enregistrer(element, EntiteSynchro.Element);
        return element.Id;
    }

    public void Dispose()
    {
        Composition.Dispose();
        try { Directory.Delete(_dossier, recursive: true); } catch { /* dossier temporaire */ }
    }
}
