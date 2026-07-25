using DeuxiemeCerveau.Windows.VueModeles;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeuxiemeCerveau.Windows.Vues;

/// <summary>
/// Marque-place d'une zone pas encore construite. Provisoire : chaque zone du §3.2 doit avoir sa
/// vraie vue avant le point d'arrêt de fin d'Étape 4.
/// </summary>
public sealed class VueAChantier : UserControl
{
    public VueAChantier(Zone zone)
    {
        Content = new Grid
        {
            Background = (Brush)Application.Current.Resources["Surface"],
            Children =
            {
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = Intitule(zone),
                            FontSize = 20,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = (Brush)Application.Current.Resources["Encre"],
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = "Vue en cours de construction.",
                            FontSize = 13,
                            Foreground = (Brush)Application.Current.Resources["Encre3"],
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                    },
                },
            },
        };
    }

    private static string Intitule(Zone zone) => zone switch
    {
        Zone.Calendrier => "Calendrier",
        Zone.Finances => "Finances",
        Zone.BudgetProjete => "Budget projeté",
        Zone.Notes => "Notes",
        Zone.Corbeille => "Corbeille",
        _ => zone.ToString(),
    };
}
