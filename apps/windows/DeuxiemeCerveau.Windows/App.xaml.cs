using Microsoft.UI.Xaml;

namespace DeuxiemeCerveau.Windows;

/// <summary>
/// Point d'entrée applicatif. La coquille affiche et saisit ; toute la logique vit dans
/// DeuxiemeCerveau.App (garde-fou-architecture, règle 2).
/// </summary>
public partial class App : Application
{
    private Window? _fenetre;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _fenetre = new FenetrePrincipale();
        _fenetre.Activate();
    }
}
