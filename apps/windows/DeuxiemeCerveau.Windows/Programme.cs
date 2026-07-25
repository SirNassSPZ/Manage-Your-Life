using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DeuxiemeCerveau.Windows;

/// <summary>
/// Main écrit à la main (DISABLE_XAML_GENERATED_MAIN) : il faudra y greffer l'instance unique et
/// les modes sans interface --rappels / --digest (D-019).
/// </summary>
public static class Programme
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Indispensable avec DISABLE_XAML_GENERATED_MAIN : sans cet appel, toute activation COM
        // WinRT échoue en E_NOINTERFACE dès « new App() ».
        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(parametres =>
        {
            var contexte = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(contexte);
            _ = new App();
        });
    }
}
