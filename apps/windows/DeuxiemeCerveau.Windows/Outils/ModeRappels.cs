using DeuxiemeCerveau.Presentation;

namespace DeuxiemeCerveau.Windows.Outils;

/// <summary>
/// Modes sans interface <c>--rappels</c> et <c>--digest</c> : la tâche planifiée les déclenche
/// quand l'app est fermée. Aucune fenêtre — c'est un toast, pas un écran.
/// <para>
/// <b>Hors ligne, délibérément.</b> Le §4 fait notifier chaque appareil « à partir des données déjà
/// synchronisées » : ce passage lit la base locale et ne parle à personne. Ni MSAL, ni HTTP, ni
/// réveil du SQL serverless — une tâche planifiée qui attendrait 61 s au réveil du matin serait un
/// mauvais échange contre des données d'une fraîcheur qui ne change rien à ce qu'on annonce.
/// </para>
/// </summary>
internal static class ModeRappels
{
    /// <summary>Les deux options qui déclenchent un passage sans interface.</summary>
    internal static readonly string[] Options =
        [PlanificateurRappels.OptionRappels, PlanificateurRappels.OptionDigest];

    /// <summary>
    /// Déroule un passage sur la base de l'utilisateur, puis rend le nombre de rappels remis.
    /// <para>
    /// À n'appeler que si <b>aucune autre instance ne tourne</b> : cette méthode ouvre
    /// <c>local.db</c>, et deux processus sur la même base sont exactement ce que D-019 interdit.
    /// </para>
    /// </summary>
    internal static int Executer(bool forcerDigest)
    {
        // Hors ligne assumé : pas d'inscription Entra, pas de pile HTTP. Composition lit la base
        // locale et applique les migrations, rien de plus.
        using var composition = Composition.Creer(new OptionsApp(), new FournisseurJetonAbsent());

        return Passer(composition, forcerDigest);
    }

    /// <summary>
    /// Le même passage, sur une composition <b>déjà ouverte</b> : c'est ce que fait l'app pendant
    /// qu'elle tourne. Les deux chemins partagent le journal, qui rend le doublon impossible —
    /// peu importe lequel des deux arrive en premier.
    /// </summary>
    internal static int Passer(Composition composition, bool forcerDigest)
    {
        // Notifications coupées dans Windows : ne rien remettre ET ne rien noter, sinon le journal
        // enterrerait des rappels que personne n'a vus.
        if (!Services.ServiceToasts.Autorisees()) return 0;

        return PlanificateurRappels.Derouler(
            composition,
            // Le journal suit la composition, pas le dossier par défaut : sous --donnees, l'un
            // lirait une base et l'autre écrirait sa mémoire ailleurs.
            JournalRappels.Ouvrir(composition.Dossier),
            DateTimeOffset.Now,
            forcerDigest,
            Services.ServiceToasts.Remettre);
    }
}
