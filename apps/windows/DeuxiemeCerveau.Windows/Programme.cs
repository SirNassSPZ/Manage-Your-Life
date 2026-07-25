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

    // async Task : RedirectActivationToAsync ne doit pas être attendu de façon bloquante sur un
    // thread STA. La documentation autorise explicitement un Main asynchrone en C#/WinUI.
    [STAThread]
    private static async Task Main(string[] args)
    {
        // Indispensable avec DISABLE_XAML_GENERATED_MAIN : sans cet appel, toute activation COM
        // WinRT échoue en E_NOINTERFACE dès « new App() ».
        WinRT.ComWrappersSupport.InitializeComWrappers();

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
    /// Vrai si une instance tourne déjà : l'activation lui est passée et ce processus s'arrête
    /// sans jamais toucher à la base.
    /// </summary>
    private static async Task<bool> RedirigeVersInstanceExistante()
    {
        var instance = AppInstance.FindOrRegisterForKey(CleInstance);
        if (instance.IsCurrent)
        {
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
