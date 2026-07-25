using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows;

/// <summary>
/// Point d'entrée applicatif. La coquille affiche et saisit ; toute la logique vit dans
/// DeuxiemeCerveau.App (garde-fou-architecture, règle 2).
/// </summary>
public partial class App : Application
{
    private Composition? _composition;
    private Window? _fenetre;
    private IntPtr _fenetrePrincipale;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Journaliser(e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args) => _ = Demarrer();

    private async Task Demarrer()
    {
        try
        {
            // Le HWND n'existe pas encore : MSAL le réclamera plus tard, à la connexion.
            _composition = await Composition.Creer(() => _fenetrePrincipale);

            var principale = new FenetrePrincipale(_composition);
            _fenetre = principale;
            _fenetrePrincipale = WinRT.Interop.WindowNative.GetWindowHandle(principale);
            principale.Closed += (_, _) => _composition?.Dispose();
            principale.Activate();

            var arguments = Environment.GetCommandLineArgs();

            // Mode outil : --vue <zone> pose la zone avant la capture, sinon on ne photographierait
            // jamais que l'écran d'ouverture.
            var iVue = Array.IndexOf(arguments, "--vue");
            if (iVue >= 0 && iVue + 1 < arguments.Length)
            {
                if (Enum.TryParse<VueModeles.Zone>(arguments[iVue + 1], ignoreCase: true, out var zone))
                    principale.Modele.Aller(zone);
            }

            if (Outils.CaptureVisuel.CheminDemande(arguments) is { } cible)
                await CapturerPuisQuitter(principale, cible);
        }
        catch (Exception ex)
        {
            // Un démarrage qui échoue en silence donne une fenêtre blanche, sans rien à se mettre
            // sous la dent : on montre la panne et on l'écrit sur disque.
            Journaliser(ex);
            MontrerPanne(ex);
        }
    }

    /// <summary>
    /// Mode outil : rend la fenêtre dans un PNG puis quitte. Sert à comparer le rendu réel à la
    /// maquette sans dépendre d'une copie d'écran (que DirectComposition rend inopérante).
    /// </summary>
    private static async Task CapturerPuisQuitter(FenetrePrincipale fenetre, string cible)
    {
        // Laisser une passe de mise en page et de rendu s'achever avant de photographier.
        await Task.Delay(1500);
        try
        {
            await Outils.CaptureVisuel.Enregistrer(fenetre.Contenu, cible);
        }
        catch (Exception ex)
        {
            Journaliser(ex);
        }
        Environment.Exit(0);
    }

    private static string CheminJournal => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeuxiemeCerveau", "demarrage.log");

    private static void Journaliser(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CheminJournal)!);
            File.AppendAllText(CheminJournal, $"""

                ===== {DateTimeOffset.Now:O} =====
                {ex}
                """);
        }
        catch
        {
            // Journaliser ne doit jamais aggraver la panne.
        }
    }

    private void MontrerPanne(Exception ex)
    {
        var fenetre = new Window { Title = "Deuxième Cerveau — démarrage impossible" };
        fenetre.Content = new ScrollViewer
        {
            Padding = new Thickness(28),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "L'application n'a pas pu démarrer.",
                        FontSize = 22,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = $"Détail écrit dans {CheminJournal}",
                        FontSize = 12,
                        Opacity = 0.7,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = ex.ToString(),
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                    },
                },
            },
        };
        _fenetre = fenetre;
        fenetre.Activate();
    }
}
