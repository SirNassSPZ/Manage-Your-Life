using DeuxiemeCerveau.Presentation;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DeuxiemeCerveau.Windows.Services;

/// <summary>
/// Remise des notifications locales (§4). <b>Ne décide rien</b> : le quoi et le quand vivent dans
/// <see cref="PlanificateurRappels"/> et <see cref="JournalRappels"/>, donc dans la couche testée
/// sur la CI Linux. Ici, seulement ce qui exige Windows (D-022).
/// </summary>
internal static class ServiceToasts
{
    /// <summary>
    /// Groupe commun : permet de retirer d'un coup ce que l'app a posté, et évite qu'un rappel
    /// remplace l'autre — c'est <c>Tag</c> qui distingue, <c>Group</c> qui rassemble.
    /// </summary>
    private const string Groupe = "deuxieme-cerveau";

    /// <summary>
    /// Ce qu'on fait quand l'utilisateur clique un toast, posé par <see cref="App"/> dès qu'une
    /// fenêtre existe. Nul dans les modes sans interface : cliquer un toast posté par la tâche
    /// planifiée relance l'exe, et c'est le chemin de lancement normal qui ouvre la fenêtre.
    /// </summary>
    internal static Action? SurClic { get; set; }

    /// <summary>
    /// Déclare l'app auprès de Windows. <b>Obligatoire</b> : l'app est non empaquetée
    /// (<c>WindowsPackageType=None</c>), elle n'a donc pas d'identité de paquet et
    /// <c>Register()</c> est ce qui lui en fabrique une — il inscrit le serveur COM qui permet à
    /// Windows de relancer l'exe au clic. Sans lui, <see cref="Remettre"/> ne montre rien.
    /// <para>
    /// Deux ordres imposés par la documentation, et ils se contredisent avec l'aisance :
    /// <c>NotificationInvoked</c> s'abonne <b>avant</b> <c>Register()</c>, et <c>Register()</c>
    /// s'appelle <b>avant</b> <c>GetActivatedEventArgs()</c> — celui-là même que la redirection
    /// d'instance unique utilise (D-019). D'où l'appel très tôt dans <see cref="Programme"/>.
    /// </para>
    /// </summary>
    internal static void Enregistrer()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) => SurClic?.Invoke();
            AppNotificationManager.Default.Register();
        }
        catch (Exception ex)
        {
            // Une notification qu'on ne sait pas déclarer ne doit pas empêcher l'app de s'ouvrir.
            App.Journaliser(ex);
        }
    }

    /// <summary>
    /// Vrai si Windows accepte de montrer nos notifications. Coupées, on ne remet rien <b>et on ne
    /// note rien</b> : marquer comme remis ce que personne n'a vu perdrait le rappel pour de bon.
    /// </summary>
    internal static bool Autorisees()
    {
        try
        {
            return AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled;
        }
        catch (Exception ex)
        {
            App.Journaliser(ex);
            return false;
        }
    }

    /// <summary>Remet un rappel. Vrai si Windows l'a accepté — c'est ce qui autorise à le noter.</summary>
    internal static bool Remettre(Rappel rappel)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument("cle", rappel.Cle)
                .AddText(rappel.Titre)
                .AddText(rappel.Corps)
                // Tag = la clé du planificateur : reposter le même rappel remplace l'ancien dans le
                // centre de notifications au lieu de l'empiler.
                .SetTag(rappel.Cle)
                .SetGroup(Groupe)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            return notification.Id != 0;
        }
        catch (Exception ex)
        {
            App.Journaliser(ex);
            return false;
        }
    }
}
