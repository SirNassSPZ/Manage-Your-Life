using CommunityToolkit.Mvvm.ComponentModel;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Les zones de la barre du haut — la navigation principale (maquette).</summary>
public enum Zone
{
    Aujourdhui,
    Calendrier,
    Finances,
    BudgetProjete,
    Projets,
    Notes,
    Corbeille,
}

/// <summary>
/// Une entrée de navigation. Le tracé est repris VERBATIM des SVG de docs/maquette.html plutôt que
/// d'une police d'icônes : c'est ce qui garantit que le rendu correspond à la maquette au trait près.
/// </summary>
public sealed partial class ElementNav : ObservableObject
{
    public required string Titre { get; init; }
    public required Zone Zone { get; init; }
    public required string Trace { get; init; }
    public string? Trace2 { get; init; }

    /// <summary>Périmètre V1 verrouillé : les zones V2 sont affichées, désactivées et étiquetées.</summary>
    public bool EstV2 { get; init; }

    // Syntaxe à champ : les « partial properties » du toolkit exigent C# 13, hors de portée du SDK .NET 8.
    [ObservableProperty]
    private bool _actif;

    [ObservableProperty]
    private string? _compteur;

    public static IReadOnlyList<ElementNav> Principales() =>
    [
        new()
        {
            Titre = "Aujourd'hui", Zone = Zone.Aujourdhui,
            Trace = "M3 8l7-5 7 5v8a1 1 0 0 1-1 1h-4v-5H8v5H4a1 1 0 0 1-1-1z",
        },
        new()
        {
            Titre = "Calendrier", Zone = Zone.Calendrier,
            Trace = "M5.5 4.5h9a2.5 2.5 0 0 1 2.5 2.5v7.5a2.5 2.5 0 0 1-2.5 2.5h-9a2.5 2.5 0 0 1-2.5-2.5V7a2.5 2.5 0 0 1 2.5-2.5z",
            Trace2 = "M3 8h14M7 3v3M13 3v3",
        },
        new()
        {
            Titre = "Finances", Zone = Zone.Finances,
            Trace = "M2.5 10a7.5 7.5 0 1 0 15 0a7.5 7.5 0 1 0-15 0",
            Trace2 = "M3 10h14M10 3v14",
        },
        new()
        {
            Titre = "Budget projeté", Zone = Zone.BudgetProjete,
            Trace = "M3 16V9M8 16V5M13 16v-4M18 16H2",
        },
        new()
        {
            Titre = "Projets", Zone = Zone.Projets, EstV2 = true,
            Trace = "M5.5 17V4M5.5 4.5h8l-2 2.6 2 2.6h-8",
        },
        new()
        {
            Titre = "Notes", Zone = Zone.Notes,
            Trace = "M5 3h7l3 3v11a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z",
            Trace2 = "M7 9h6M7 12h6",
        },
        new()
        {
            Titre = "Corbeille", Zone = Zone.Corbeille,
            Trace = "M4 6h12M8 6V4h4v2M6 6l1 10h6l1-10",
        },
    ];
}
