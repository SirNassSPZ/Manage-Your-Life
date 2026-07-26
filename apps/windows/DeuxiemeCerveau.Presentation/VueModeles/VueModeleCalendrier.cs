using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Une occurrence posée dans une case du calendrier.</summary>
public sealed record PastilleAgenda(string Titre, string? Montant, TypeElement Type);

/// <summary>
/// Ce que montre la zone Calendrier. Les deux premiers sont deux lectures des mêmes occurrences
/// (§5.4) ; le troisième gère les calendriers eux-mêmes — catégorie = calendrier (§3.3).
/// </summary>
public enum ModeCalendrier { Mois, SeptJours, Gestion }

/// <summary>
/// Un jour de la vue « 7 prochains jours ». Depuis D-028 cette vue est une <b>grille</b> de sept
/// colonnes, pas une liste : on ne lit pas une semaine en la faisant défiler.
/// </summary>
/// <param name="Intitule">Libellé long — sert l'accessibilité, pas l'affichage serré de la colonne.</param>
/// <param name="Abrege">« LUN », en-tête de colonne.</param>
/// <param name="Numero">Le numéro du jour, comme dans la grille du mois.</param>
public sealed record JourSemaine(
    string Intitule,
    string Abrege,
    string Numero,
    string Resume,
    bool EstAujourdhui,
    IReadOnlyList<PastilleAgenda> Pastilles);

/// <summary>
/// Une case de la grille mensuelle. Six semaines pleines sont toujours rendues (42 cases) : une
/// grille dont la hauteur saute d'un mois à l'autre donne une impression de bougé à chaque flèche.
/// </summary>
public sealed record CaseJour(
    string Numero,
    bool HorsMois,
    bool EstAujourdhui,
    IReadOnlyList<PastilleAgenda> Pastilles,
    string? Debordement);

/// <summary>
/// Calendrier principal unifié (§5.4) : les rendez-vous ET les échéances financières, mois par mois.
/// <para>
/// Les RRULE sont développées par <c>ServiceCalendrier</c> <b>pour l'affichage seulement</b> (§4) —
/// aucun calcul d'argent ici, le budget projeté reste au serveur (règle 9). Tout est local : cette
/// vue fonctionne hors ligne de bout en bout (filet 1).
/// </para>
/// </summary>
public sealed partial class VueModeleCalendrier : ObservableObject
{
    /// <summary>Au-delà, la case affiche « +n » plutôt que de pousser la grille.</summary>
    private const int PastillesParCase = 3;

    private readonly Composition _composition;
    private readonly Func<IReadOnlySet<Guid>?> _categoriesVisibles;

    private DateOnly _mois;

    public VueModeleCalendrier(Composition composition, Func<IReadOnlySet<Guid>?> categoriesVisibles)
    {
        _composition = composition;
        _categoriesVisibles = categoriesVisibles;

        var today = DateOnly.FromDateTime(DateTime.Now);
        _mois = new DateOnly(today.Year, today.Month, 1);
        Charger();
    }

    public ObservableCollection<CaseJour> Cases { get; } = [];

    /// <summary>Les sept prochains jours, jours vides compris — l'absence de mouvement est une information.</summary>
    public ObservableCollection<JourSemaine> Semaine { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstMois))]
    [NotifyPropertyChangedFor(nameof(EstSeptJours))]
    [NotifyPropertyChangedFor(nameof(EstGestion))]
    private ModeCalendrier _mode = ModeCalendrier.Mois;

    public bool EstMois => Mode == ModeCalendrier.Mois;
    public bool EstSeptJours => Mode == ModeCalendrier.SeptJours;
    public bool EstGestion => Mode == ModeCalendrier.Gestion;

    /// <summary>Bascule d'affichage. La navigation par mois n'a pas de sens sur sept jours glissants.</summary>
    [RelayCommand]
    private void ChoisirMode(ModeCalendrier mode)
    {
        Mode = mode;
        if (mode == ModeCalendrier.SeptJours) RevenirAujourdhui();
        Charger();
    }

    /// <summary>« Juillet ».</summary>
    [ObservableProperty]
    private string _moisAffiche = "";

    /// <summary>« 2026 » — séparé du mois pour le contraste de graisse de la maquette.</summary>
    [ObservableProperty]
    private string _anneeAffichee = "";

    /// <summary>Vrai quand le mois affiché n'est pas le mois courant : « Aujourd'hui » a alors un sens.</summary>
    [ObservableProperty]
    private bool _horsMoisCourant;

    [RelayCommand]
    private void MoisPrecedent() { _mois = _mois.AddMonths(-1); Charger(); }

    [RelayCommand]
    private void MoisSuivant() { _mois = _mois.AddMonths(1); Charger(); }

    [RelayCommand]
    private void RevenirAujourdhui()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        _mois = new DateOnly(today.Year, today.Month, 1);
        Charger();
    }

    public void Charger()
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        MoisAffiche = fr.TextInfo.ToTitleCase(_mois.ToString("MMMM", fr));
        AnneeAffichee = _mois.ToString("yyyy", fr);

        var aujourdhui = DateOnly.FromDateTime(DateTime.Now);
        HorsMoisCourant = _mois.Year != aujourdhui.Year || _mois.Month != aujourdhui.Month;

        // La grille commence au lundi de la semaine du 1er (WKST=MO, D-003) et court sur 6 semaines.
        var decalage = ((int)_mois.DayOfWeek + 6) % 7;
        var premiereCase = _mois.AddDays(-decalage);
        var derniereCase = premiereCase.AddDays(41);

        var debut = new DateTimeOffset(premiereCase.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fin = new DateTimeOffset(derniereCase.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var occurrences = _composition.Acces.Lire(() =>
            _composition.Calendrier.Occurrences(debut, fin, _categoriesVisibles()));

        // Un seul regroupement par jour LOCAL : l'occurrence est stockée en UTC, l'utilisateur la
        // voit dans son fuseau (§3.5).
        var parJour = occurrences
            .GroupBy(o => DateOnly.FromDateTime(o.InstantUtc.ToLocalTime().DateTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        Cases.Clear();
        for (var i = 0; i < 42; i++)
        {
            var jour = premiereCase.AddDays(i);
            parJour.TryGetValue(jour, out var duJour);

            var pastilles = (duJour ?? [])
                .Take(PastillesParCase)
                .Select(o => new PastilleAgenda(
                    o.Titre,
                    o.MontantCentimes is { } c && o.Sens is { } s ? Format.EurosCompact(c, s) : null,
                    o.Type))
                .ToList();

            var reste = (duJour?.Count ?? 0) - pastilles.Count;

            Cases.Add(new CaseJour(
                Numero: jour.Day.ToString(),
                HorsMois: jour.Month != _mois.Month,
                EstAujourdhui: jour == aujourdhui,
                Pastilles: pastilles,
                Debordement: reste > 0 ? $"+{reste}" : null));
        }

        ChargerSemaine(aujourdhui, fr);
    }

    /// <summary>
    /// Les sept prochains jours à partir d'aujourd'hui. Vue glissante, indépendante du mois
    /// affiché : elle répond à « qu'est-ce qui arrive bientôt », pas à « que contient juillet ».
    /// Les occurrences viennent du même service, avec les mêmes filtres.
    /// </summary>
    private void ChargerSemaine(DateOnly aujourdhui, System.Globalization.CultureInfo fr)
    {
        var debut = new DateTimeOffset(aujourdhui.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fin = new DateTimeOffset(aujourdhui.AddDays(6).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var parJour = _composition.Acces
            .Lire(() => _composition.Calendrier.Occurrences(debut, fin, _categoriesVisibles()))
            .GroupBy(o => DateOnly.FromDateTime(o.InstantUtc.ToLocalTime().DateTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        Semaine.Clear();
        for (var i = 0; i < 7; i++)
        {
            var jour = aujourdhui.AddDays(i);
            parJour.TryGetValue(jour, out var duJour);
            var n = duJour?.Count ?? 0;

            Semaine.Add(new JourSemaine(
                Intitule: Format.Capitales(i switch
                {
                    0 => "Aujourd'hui · " + Format.JourCourt(jour),
                    1 => "Demain · " + Format.JourCourt(jour),
                    _ => Format.JourLong(jour),
                }),
                // Trois lettres, comme les en-têtes de la grille du mois : les deux lectures
                // partagent la même grammaire visuelle.
                Abrege: Format.Capitales(jour.ToString("ddd", fr)).TrimEnd('.'),
                Numero: jour.Day.ToString(),
                Resume: n switch { 0 => "Rien de prévu", 1 => "1 mouvement", _ => $"{n} mouvements" },
                EstAujourdhui: i == 0,
                Pastilles: (duJour ?? [])
                    .Select(o => new PastilleAgenda(
                        o.Titre,
                        o.MontantCentimes is { } c && o.Sens is { } s ? Format.EurosSigne(c, s) : null,
                        o.Type))
                    .ToList()));
        }
    }
}
