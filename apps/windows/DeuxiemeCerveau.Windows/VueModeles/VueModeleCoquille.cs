using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Windows.Presentation;

namespace DeuxiemeCerveau.Windows.VueModeles;

/// <summary>Entrée de la barre latérale contextuelle (sous-vue de la zone active).</summary>
public sealed partial class SousVue : ObservableObject
{
    public required string Titre { get; init; }
    public required string Trace { get; init; }
    public string? Trace2 { get; init; }
    public bool EstV2 { get; init; }

    [ObservableProperty]
    private bool _actif;
}

/// <summary>Un calendrier affichable/masquable — catégorie = label = calendrier (§3.3).</summary>
public sealed partial class FiltreCalendrier : ObservableObject
{
    public required Guid Id { get; init; }
    public required string Nom { get; init; }
    public required string Couleur { get; init; }

    [ObservableProperty]
    private bool _visible = true;
}

/// <summary>
/// La coquille : barre du haut (zones) + barre latérale contextuelle (sous-vues), comme la maquette.
/// Elle ne fait qu'aiguiller — chaque vue parle à son propre service.
/// </summary>
public sealed partial class VueModeleCoquille : ObservableObject
{
    private readonly Composition _composition;

    public VueModeleCoquille(Composition composition)
    {
        _composition = composition;
        Principales = [.. ElementNav.Principales()];
        Accueil = new VueModeleAujourdhui(composition);
        Onboarding = new VueModeleOnboarding(composition);
        Aller(Zone.Aujourdhui);
    }

    /// <summary>Vrai tant que le solde de référence n'est pas posé (§3.4).</summary>
    public bool OnboardingRequis() =>
        _composition.Acces.Lire(() => _composition.Demarrage.OnboardingRequis());

    public ObservableCollection<ElementNav> Principales { get; }
    public ObservableCollection<SousVue> SousVues { get; } = [];
    public ObservableCollection<FiltreCalendrier> Calendriers { get; } = [];

    public VueModeleAujourdhui Accueil { get; }
    public VueModeleOnboarding Onboarding { get; }

    [ObservableProperty]
    private Zone _zone = Zone.Aujourdhui;

    [ObservableProperty]
    private string _soldeEntete = "—";

    [ObservableProperty]
    private bool _entetePossedeSolde;

    [ObservableProperty]
    private string _titreSousVues = "Vue";

    /// <summary>Étiquette d'état de synchro, dérivée de l'outbox (aucun service ne la fournit).</summary>
    [ObservableProperty]
    private string _etatSynchro = "";

    [RelayCommand]
    private void Naviguer(ElementNav cible)
    {
        // Périmètre V1 verrouillé : les zones V2 sont visibles mais inertes.
        if (cible.EstV2) return;
        Aller(cible.Zone);
    }

    public void Aller(Zone zone)
    {
        Zone = zone;
        foreach (var entree in Principales) entree.Actif = entree.Zone == zone;

        SousVues.Clear();
        TitreSousVues = zone switch
        {
            Zone.Finances => "Finances",
            Zone.Calendrier => "Mes calendriers",
            _ => "Vue",
        };

        foreach (var sousVue in SousVuesDe(zone)) SousVues.Add(sousVue);
        if (SousVues.Count > 0) SousVues[0].Actif = true;

        Rafraichir();
    }

    private static IEnumerable<SousVue> SousVuesDe(Zone zone) => zone switch
    {
        Zone.Aujourdhui =>
        [
            new() { Titre = "Aujourd'hui", Trace = "M3 10a7 7 0 1 0 14 0a7 7 0 1 0-14 0" },
            new() { Titre = "7 prochains jours", Trace = "M5.5 4.5h9a2.5 2.5 0 0 1 2.5 2.5v7.5a2.5 2.5 0 0 1-2.5 2.5h-9a2.5 2.5 0 0 1-2.5-2.5V7a2.5 2.5 0 0 1 2.5-2.5z", Trace2 = "M3 8h14M7 3v3M13 3v3" },
        ],
        Zone.Finances =>
        [
            new() { Titre = "Vue d'ensemble", Trace = "M3 3h6v6H3zM11 3h6v6h-6zM3 11h6v6H3zM11 11h6v6h-6z" },
            new() { Titre = "Entrées", Trace = "M10 3v9M6 8l4 4 4-4M4 16h12" },
            new() { Titre = "Sorties", Trace = "M10 13V4M6 8l4-4 4 4M4 16h12" },
            new() { Titre = "Par catégorie", Trace = "M3 10a7 7 0 1 0 14 0a7 7 0 1 0-14 0", Trace2 = "M10 10V3M10 10l6 3.5" },
            new() { Titre = "Envies d'achat", Trace = "M10 16S3.5 12 3.5 7.5A3.3 3.3 0 0 1 10 5a3.3 3.3 0 0 1 6.5 2.5C16.5 12 10 16 10 16z" },
            new() { Titre = "Enveloppes", EstV2 = true, Trace = "M5 5h10a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z", Trace2 = "M3.5 6l6.5 5 6.5-5" },
        ],
        _ => [],
    };

    /// <summary>Recharge la vue active et l'entête. À rappeler après toute écriture.</summary>
    public void Rafraichir()
    {
        if (Zone == Zone.Aujourdhui) Accueil.Charger();

        var solde = _composition.Acces.Lire(() => _composition.Aujourdhui.SoldeDeReference());
        EntetePossedeSolde = solde is not null;
        SoldeEntete = solde is null ? "—" : Format.Euros(solde.Centimes);

        ChargerCalendriers();

        var enAttente = _composition.Acces.Lire(() => _composition.Depot.Outbox().Count);
        EtatSynchro = enAttente switch
        {
            0 when !_composition.SynchroPossible => "Hors ligne",
            0 => "À jour",
            1 => "1 changement en attente",
            _ => $"{enAttente} changements en attente",
        };
    }

    private void ChargerCalendriers()
    {
        var categories = _composition.Acces.Lire(() => _composition.Lecture.Categories());
        Calendriers.Clear();
        foreach (var categorie in categories)
            Calendriers.Add(new FiltreCalendrier
            {
                Id = categorie.Id,
                Nom = categorie.Nom,
                Couleur = string.IsNullOrWhiteSpace(categorie.Couleur) ? "#7A6AA6" : categorie.Couleur,
            });
    }
}
