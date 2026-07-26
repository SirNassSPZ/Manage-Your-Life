using DeuxiemeCerveau.Presentation;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DeuxiemeCerveau.Windows;

/// <summary>
/// Main écrit à la main (<c>DISABLE_XAML_GENERATED_MAIN</c>, D-019).
/// <para>
/// Un seul motif l'exige réellement : <b>l'instance unique</b>. La documentation Windows App SDK
/// est explicite — la détection et la redirection doivent se faire « as early as possible, and
/// before initializing any windows », ce que le <c>Main</c> généré ne permet pas.
/// </para>
/// <para>
/// Et ce n'est pas un confort : <c>BaseLocale</c> détient UNE connexion SQLite sur
/// <c>%LOCALAPPDATA%\DeuxiemeCerveau\local.db</c>. Deux processus lancés en parallèle — un toast,
/// une tâche planifiée, un double-clic — écriraient dans la même base et la même outbox. C'est un
/// risque pour les filets 1 et 2, pas une gêne d'ergonomie.
/// </para>
/// </summary>
public static class Programme
{
    /// <summary>Clé d'instance. Constante : toutes les activations visent le même processus.</summary>
    private const string CleInstance = "deuxieme-cerveau";

    /// <summary>
    /// Modes outil : ils doivent tourner dans LEUR propre processus. Rediriger une capture ou un
    /// contrôle de parité vers une fenêtre déjà ouverte ne produirait ni image ni rapport.
    /// </summary>
    private static readonly string[] ModesOutil =
        [ScenariosParite.Option, Outils.CaptureVisuel.Option];

    /// <summary>Levée à l'activation de l'instance en place — posée par <see cref="App"/>.</summary>
    internal static Action? SurActivation { get; set; }

    // async Task : RedirectActivationToAsync ne doit pas être attendu de façon bloquante sur un
    // thread STA. La documentation autorise explicitement un Main asynchrone en C#/WinUI.
    [STAThread]
    private static async Task Main(string[] args)
    {
        // Indispensable avec DISABLE_XAML_GENERATED_MAIN : sans cet appel, toute activation COM
        // WinRT échoue en E_NOINTERFACE dès « new App() ».
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // AVANT toute lecture d'activation : la documentation impose Register() avant
        // GetActivatedEventArgs(), que la redirection d'instance unique appelle juste en dessous.
        Services.ServiceToasts.Enregistrer();

        // Modes de notification : ils veulent la base de l'utilisateur, pas un dossier jetable —
        // c'est ce qui les sépare des modes outil, et ce qui leur interdit d'en être.
        if (args.Any(Outils.ModeRappels.Options.Contains))
        {
            // L'app est ouverte : elle seule touche à local.db (D-019), et elle fait déjà ses
            // passages toute seule. Ce processus-ci s'efface sans rien ouvrir.
            if (InstanceEnPlace() is null)
                Outils.ModeRappels.Executer(args.Contains(PlanificateurRappels.OptionDigest));
            return;
        }

        if (!args.Any(ModesOutil.Contains) && await RedirigeVersInstanceExistante())
            return;

        Application.Start(parametres =>
        {
            var contexte = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(contexte);
            _ = new App();
        });
    }

    /// <summary>
    /// L'instance déjà enregistrée, ou null. <b>Ne s'enregistre pas soi-même</b>, contrairement à
    /// <c>FindOrRegisterForKey</c> : un mode de notification qui prendrait la clé deviendrait la
    /// cible des redirections pendant la seconde où il tourne, et avalerait un double-clic de
    /// l'utilisateur sans jamais ouvrir de fenêtre.
    /// </summary>
    private static AppInstance? InstanceEnPlace() =>
        AppInstance.GetInstances().FirstOrDefault(i => i.Key == CleInstance);

    /// <summary>
    /// Vrai si une instance tourne déjà : l'activation lui est passée et ce processus s'arrête
    /// sans jamais toucher à la base.
    /// </summary>
    private static async Task<bool> RedirigeVersInstanceExistante()
    {
        var instance = AppInstance.FindOrRegisterForKey(CleInstance);
        if (instance.IsCurrent)
        {
            // Recevoir une redirection ne fait rien tout seul : sans ce relais, un second
            // lancement — ou le clic sur un toast, qui relance l'exe — disparaîtrait en silence.
            instance.Activated += (_, _) => SurActivation?.Invoke();

            // Libérer la clé à l'arrêt : sans ça, une instance en cours de fermeture peut encore
            // recevoir une redirection et la perdre.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { AppInstance.GetCurrent().UnregisterKey(); } catch { /* on s'arrête déjà */ }
            };
            return false;
        }

        await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
        return true;
    }
}
