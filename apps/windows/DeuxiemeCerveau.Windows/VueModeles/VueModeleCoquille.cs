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
        Budget = new VueModeleBudget(composition);

        // Le calendrier lit les filtres ICI plutôt que d'en tenir une copie : deux listes de
        // catégories qui divergent, c'est un filtre qui ment.
        Calendrier = new VueModeleCalendrier(composition, CategoriesVisibles);

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
    public VueModeleBudget Budget { get; }
    public VueModeleCalendrier Calendrier { get; }

    /// <summary>
    /// Les catégories cochées, ou null si elles le sont toutes — le service traite null comme
    /// « aucun filtre » et évite ainsi de parcourir un ensemble pour rien.
    /// </summary>
    private IReadOnlySet<Guid>? CategoriesVisibles() =>
        Calendriers.All(c => c.Visible) ? null : Calendriers.Where(c => c.Visible).Select(c => c.Id).ToHashSet();

    /// <summary>Affiche ou masque un calendrier (§5.4) et recharge la grille.</summary>
    [RelayCommand]
    private void BasculerCalendrier(FiltreCalendrier filtre)
    {
        filtre.Visible = !filtre.Visible;
        Calendrier.Charger();
    }

    /// <summary>Faux quand la zone active n'a pas de sous-vues : l'intitulé ne doit pas rester seul.</summary>
    [ObservableProperty]
    private bool _aSousVues;

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

    /// <summary>Adresse du compte Entra connecté, ou null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeutSeConnecter))]
    private string? _compte;

    /// <summary>Faux quand aucune inscription Entra n'est configurée : le bouton n'a alors aucun sens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeutSeConnecter))]
    private bool _connexionOfferte;

    /// <summary>Le bouton « Se connecter » ne s'affiche que s'il y a de quoi se connecter, et pas déjà.</summary>
    public bool PeutSeConnecter => ConnexionOfferte && Compte is null;

    [ObservableProperty]
    private bool _occupe;

    /// <summary>
    /// Connexion Entra — un geste EXPLICITE de l'utilisateur. C'est le seul endroit qui a le droit
    /// d'ouvrir un navigateur : la synchro de fond, elle, n'utilise que l'acquisition silencieuse.
    /// </summary>
    [RelayCommand]
    private async Task Connecter()
    {
        if (Occupe) return;
        Occupe = true;
        try
        {
            await _composition.Jetons.Connecter();
            await RelireCompte();
        }
        finally { Occupe = false; }
    }

    [RelayCommand]
    private async Task Deconnecter()
    {
        if (Occupe) return;
        Occupe = true;
        try
        {
            await _composition.Jetons.Deconnecter();
            await RelireCompte();
        }
        finally { Occupe = false; }
    }

    public async Task RelireCompte()
    {
        ConnexionOfferte = _composition.Options.Api.EstConfiguree && _composition.Jetons.Configure;
        Compte = ConnexionOfferte ? await _composition.Jetons.CompteConnecte() : null;
        Rafraichir();
    }

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

        // Pas de cas « Calendrier » ici : ses sous-vues SONT les calendriers, déjà listés dans leur
        // propre section. L'y répéter affichait « Mes calendriers » deux fois, dont une à vide.
        TitreSousVues = zone switch
        {
            Zone.Finances => "Finances",
            _ => "Vue",
        };

        foreach (var sousVue in SousVuesDe(zone)) SousVues.Add(sousVue);
        if (SousVues.Count > 0) SousVues[0].Actif = true;
        ASousVues = SousVues.Count > 0;

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
        // Les filtres D'ABORD : la grille du calendrier les lit, elle ne peut pas partir de l'état
        // précédent.
        ChargerCalendriers();

        if (Zone == Zone.Aujourdhui) Accueil.Charger();
        if (Zone == Zone.Calendrier) Calendrier.Charger();

        var solde = _composition.Acces.Lire(() => _composition.Aujourdhui.SoldeDeReference());
        EntetePossedeSolde = solde is not null;
        SoldeEntete = solde is null ? "—" : Format.Euros(solde.Centimes);

        var enAttente = _composition.Acces.Lire(() => _composition.Depot.Outbox().Count);
        EtatSynchro = (enAttente, Compte) switch
        {
            (_, null) when !_composition.SynchroPossible => "Hors ligne",
            (0, null) => "Non connecté",
            (1, null) => "1 changement, non connecté",
            (_, null) => $"{enAttente} changements, non connecté",
            (0, _) => "À jour",
            (1, _) => "1 changement en attente",
            _ => $"{enAttente} changements en attente",
        };
    }

    private void ChargerCalendriers()
    {
        // Ce que l'utilisateur a masqué doit le RESTER. La liste étant reconstruite à chaque
        // rafraîchissement, sans cette mémoire le moindre changement de vue ou de saisie
        // rallumerait en silence les calendriers qu'il venait d'éteindre.
        var masques = Calendriers.Where(c => !c.Visible).Select(c => c.Id).ToHashSet();

        var categories = _composition.Acces.Lire(() => _composition.Lecture.Categories());
        Calendriers.Clear();
        foreach (var categorie in categories)
            Calendriers.Add(new FiltreCalendrier
            {
                Id = categorie.Id,
                Nom = categorie.Nom,
                Couleur = string.IsNullOrWhiteSpace(categorie.Couleur) ? "#7A6AA6" : categorie.Couleur,
                Visible = !masques.Contains(categorie.Id),
            });
    }
}
