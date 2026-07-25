using System.Text;
using DeuxiemeCerveau.Presentation;
using Microsoft.Extensions.Configuration;
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
            // L'hôte assemble ce qui exige Windows, la présentation ignore tout des deux (D-022) :
            // la lecture du fichier de configuration, MSAL, et la pile HTTP.
            var options = LireConfiguration();

            // Le HWND n'existe pas encore : MSAL le réclamera plus tard, à la connexion.
            var jetons = await Services.FournisseurJetonMsal.Creer(options.Entra, () => _fenetrePrincipale)
                .ConfigureAwait(true);

            static HttpMessageHandler Pile(IFournisseurJeton j) => new Services.ManipulateurJeton(j)
            {
                InnerHandler = new Services.ManipulateurReessai { InnerHandler = new HttpClientHandler() },
            };

            // Mode outil : --parite déroule les scénarios §12 sur l'application réelle, contre le
            // serveur déployé, dans des dossiers de données jetables — les données de l'utilisateur
            // ne sont ni lues ni écrites. Aucune fenêtre : c'est un rapport, pas un écran.
            if (Environment.GetCommandLineArgs().Contains(ScenariosParite.Option))
            {
                var rapport = ScenariosParite.Rapport(
                    await ScenariosParite.Executer(options, jetons, Pile));
                Console.WriteLine(rapport);
                // UTF-8 explicite : l'encodage par défaut de la console Windows massacre les accents.
                File.WriteAllText(
                    Path.Combine(Composition.DossierParDefaut, "parite.txt"), rapport, Encoding.UTF8);
                Environment.Exit(rapport.Contains("Les trois scénarios passent.") ? 0 : 1);
            }

            _composition = Composition.Creer(options, jetons, manipulateurApi: Pile);

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
                if (Enum.TryParse<DeuxiemeCerveau.Presentation.VueModeles.Zone>(arguments[iVue + 1], ignoreCase: true, out var zone))
                    principale.Modele.Aller(zone);
            }

            // --mode <Mois|SeptJours|Gestion> : la zone Calendrier porte trois lectures, et la
            // capture doit pouvoir atteindre les deux autres que celle d'ouverture.
            var iMode = Array.IndexOf(arguments, "--mode");
            if (iMode >= 0 && iMode + 1 < arguments.Length
                && Enum.TryParse<DeuxiemeCerveau.Presentation.VueModeles.ModeCalendrier>(
                    arguments[iMode + 1], ignoreCase: true, out var mode))
            {
                // On passe par la sous-vue, pas par le mode directement : c'est le chemin que
                // l'utilisateur emprunte, et lui seul met aussi à jour la barre latérale.
                var titre = mode switch
                {
                    DeuxiemeCerveau.Presentation.VueModeles.ModeCalendrier.SeptJours => "7 prochains jours",
                    DeuxiemeCerveau.Presentation.VueModeles.ModeCalendrier.Gestion => "Gérer les calendriers",
                    _ => "Grille du mois",
                };
                if (principale.Modele.SousVues.FirstOrDefault(s => s.Titre == titre) is { } sousVue)
                    principale.Modele.ChoisirSousVueCommand.Execute(sousVue);
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
    /// Identifiants PUBLICS uniquement (règle 16) : URL de l'API et inscription Entra. Le fichier
    /// local, gitignoré, surcharge sans toucher au dépôt.
    /// </summary>
    private static OptionsApp LireConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.local.json", optional: true)
            .Build()
            .Get<OptionsApp>() ?? new OptionsApp();

    /// <summary>
    /// Mode outil : rend la fenêtre dans un PNG puis quitte. Sert à comparer le rendu réel à la
    /// maquette sans dépendre d'une copie d'écran (que DirectComposition rend inopérante).
    /// </summary>
    private static async Task CapturerPuisQuitter(FenetrePrincipale fenetre, string cible)
    {
        // Laisser une passe de mise en page et de rendu s'achever avant de photographier.
        // --capture-delai <ms> pour attendre un chargement réseau (réveil SQL serverless : ~61 s).
        var arguments = Environment.GetCommandLineArgs();
        var iDelai = Array.IndexOf(arguments, "--capture-delai");
        var delai = iDelai >= 0 && iDelai + 1 < arguments.Length
            && int.TryParse(arguments[iDelai + 1], out var ms) ? ms : 1500;
        await Task.Delay(delai);
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
