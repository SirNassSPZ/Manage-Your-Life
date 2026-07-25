using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Une occurrence posée dans une case du calendrier.</summary>
public sealed record PastilleAgenda(string Titre, string? Montant, TypeElement Type);

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
    }
}
