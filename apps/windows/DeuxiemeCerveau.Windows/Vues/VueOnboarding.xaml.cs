using DeuxiemeCerveau.Windows.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class VueOnboarding : UserControl
{
    public VueOnboarding(VueModeleOnboarding modele)
    {
        Modele = modele;
        InitializeComponent();
    }

    public VueModeleOnboarding Modele { get; }

    private void SurValider(object expediteur, Microsoft.UI.Xaml.RoutedEventArgs args) => Modele.Valider();
}
