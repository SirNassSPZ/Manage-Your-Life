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

            // --donnees <chemin> : monter l'app sur un dossier de données autre que celui de
            // l'utilisateur. C'est ce qui permet de photographier un écran garni sans jamais
            // écrire une ligne de démonstration dans la vraie base.
            var argsDemarrage = Environment.GetCommandLineArgs();
            var iDonnees = Array.IndexOf(argsDemarrage, "--donnees");
            var dossier = iDonnees >= 0 && iDonnees + 1 < argsDemarrage.Length
                ? argsDemarrage[iDonnees + 1]
                : null;

            _composition = Composition.Creer(options, jetons, dossier, Pile);

            var principale = new FenetrePrincipale(_composition);
            _fenetre = principale;
            _fenetrePrincipale = WinRT.Interop.WindowNative.GetWindowHandle(principale);
            principale.Closed += (_, _) => _composition?.Dispose();
            principale.Activate();

            // Un second lancement, ou le clic sur un toast : les deux relancent l'exe, qui redirige
            // vers cette instance. Sans ces deux relais, il ne se passerait rien à l'écran.
            Programme.SurActivation = AuPremierPlan;
            Services.ServiceToasts.SurClic = AuPremierPlan;

            // Tant que l'app tourne, c'est elle qui notifie : la tâche planifiée s'efface devant
            // elle (D-019 — un seul processus sur local.db).
            DemarrerPassagesRappels(_composition);

            var arguments = Environment.GetCommandLineArgs();

            // Mode outil : --vue <zone> pose la zone avant la capture, sinon on ne photographierait
            // jamais que l'écran d'ouverture.
            var iVue = Array.IndexOf(arguments, "--vue");
            if (iVue >= 0 && iVue + 1 < arguments.Length)
            {
                if (Enum.TryParse<DeuxiemeCerveau.Presentation.VueModeles.Zone>(arguments[iVue + 1], ignoreCase: true, out var zone))
                    principale.Modele.Aller(zone);
            }

            // --mode <sous-vue> : plusieurs zones portent plusieurs lectures des mêmes données, et
            // la capture doit pouvoir atteindre les autres que celle d'ouverture.
            var iMode = Array.IndexOf(arguments, "--mode");
            if (iMode >= 0 && iMode + 1 < arguments.Length)
            {
                var demande = arguments[iMode + 1];

                // Les trois noms historiques du calendrier restent acceptés ; sinon on désigne la
                // sous-vue par son intitulé, ce qui vaut pour toutes les zones sans table à tenir.
                var titre = demande.ToUpperInvariant() switch
                {
                    "SEPTJOURS" => "7 prochains jours",
                    "GESTION" => "Gérer les calendriers",
                    "MOIS" => "Grille du mois",
                    _ => demande,
                };

                // On passe par la sous-vue, pas par le mode directement : c'est le chemin que
                // l'utilisateur emprunte, et lui seul met aussi à jour la barre latérale.
                if (principale.Modele.SousVues.FirstOrDefault(
                        s => Simplifie(s.Titre) == Simplifie(titre)) is { } sousVue)
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
    /// Réduit un intitulé à ses lettres nues, sans accents ni espaces. Les intitulés de sous-vues
    /// sont accentués (« Par catégorie », « Gérer les calendriers ») et la page de codes de la
    /// console les massacre avant même que l'argument n'arrive : comparer tel quel ne trouve rien.
    /// </summary>
    private static string Simplifie(string texte) => new(texte
        .Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => char.IsLetterOrDigit(c)
                 && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
        .Select(char.ToLowerInvariant)
        .ToArray());

    /// <summary>
    /// Ramène la fenêtre devant. Une fenêtre réduite doit d'abord être restaurée :
    /// <c>Activate()</c> seul la laisse dans la barre des tâches, à clignoter.
    /// </summary>
    private void AuPremierPlan()
    {
        if (_fenetre is not { } fenetre) return;

        // L'activation arrive d'un thread de fond : tout ce qui touche à la fenêtre repasse par le
        // fil de l'interface, sinon c'est un RPC_E_WRONG_THREAD.
        fenetre.DispatcherQueue.TryEnqueue(() =>
        {
            if (fenetre.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
                { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presentateur)
                presentateur.Restore();

            fenetre.Activate();
        });
    }

    /// <summary>
    /// Passages de notification pendant que l'app tourne (§4) : un au démarrage, puis un par heure.
    /// <para>
    /// Sans la minuterie, une app laissée ouverte depuis lundi ne notifierait plus rien de la
    /// semaine — la tâche planifiée, elle, s'efface tant que ce processus détient la base.
    /// L'heure est un compromis franc : assez fin pour ne pas rater un passage de minuit, assez
    /// large pour rester invisible.
    /// </para>
    /// </summary>
    private static void DemarrerPassagesRappels(Composition composition)
    {
        // Hors du fil de l'interface : le passage lit la base, et l'interface n'attend jamais.
        static void Passer(Composition composition) => _ = Task.Run(() =>
        {
            try { Outils.ModeRappels.Passer(composition, forcerDigest: false); }
            catch (Exception ex) { Journaliser(ex); }
        });

        Passer(composition);

        var minuterie = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        minuterie.Tick += (_, _) => Passer(composition);
        minuterie.Start();
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

    internal static void Journaliser(Exception ex)
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
