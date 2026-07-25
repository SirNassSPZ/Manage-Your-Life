using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Presentation;

/// <summary>Une notification prête à être remise — titre, corps, et de quoi la dédoublonner.</summary>
public sealed record Rappel(string Cle, string Titre, string Corps);

/// <summary>
/// Planifie les notifications locales (§4) à partir des données <b>déjà synchronisées</b> : chaque
/// appareil notifie pour lui-même, il n'y a pas de push serveur (reporté V3).
/// <para>
/// Toute la décision « quoi notifier, quand » vit ici, donc dans la couche testée. Le côté Windows
/// ne fait que <b>remettre</b> ce que cette classe a décidé — c'est ce qui permet à l'app Apple de
/// reproduire le même comportement depuis la spec plutôt que depuis le code.
/// </para>
/// </summary>
public static class PlanificateurRappels
{
    /// <summary>Options de ligne de commande des modes sans interface (D-019).</summary>
    public const string OptionRappels = "--rappels";
    public const string OptionDigest = "--digest";

    /// <summary>
    /// Échéances de demain (§13 : « rappels par notifications locales — rendez-vous et échéances »).
    /// La veille, pas le jour même : un rappel qui arrive le matin d'un prélèvement ne sert à rien.
    /// </summary>
    public static IReadOnlyList<Rappel> Echeances(Composition composition, DateTimeOffset maintenant)
    {
        var demain = DateOnly.FromDateTime(maintenant.LocalDateTime).AddDays(1);
        var debut = new DateTimeOffset(demain.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fin = new DateTimeOffset(demain.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var occurrences = composition.Acces.Lire(() => composition.Calendrier.Occurrences(debut, fin));

        return [.. occurrences.Select(o => new Rappel(
            // La clé porte le jour : la même échéance ne notifie qu'une fois, même si le mode
            // --rappels est déclenché plusieurs fois dans la journée.
            Cle: $"echeance-{o.ElementId:N}-{demain:yyyy-MM-dd}",
            Titre: Titre(o),
            Corps: Corps(o)))];
    }

    /// <summary>
    /// Digest hebdomadaire (D-018 #4) : le point du dimanche. Rare et actionnable — c'est ce qui
    /// crée le rendez-vous d'habitude sans devenir du bruit.
    /// </summary>
    public static Rappel? Digest(Composition composition, DateTimeOffset maintenant)
    {
        var aujourdhui = DateOnly.FromDateTime(maintenant.LocalDateTime);
        var debut = new DateTimeOffset(aujourdhui.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fin = new DateTimeOffset(aujourdhui.AddDays(6).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var occurrences = composition.Acces.Lire(() => composition.Calendrier.Occurrences(debut, fin));
        if (occurrences.Count == 0) return null;

        long sorties = 0, entrees = 0;
        foreach (var o in occurrences)
        {
            if (o.MontantCentimes is not { } c || o.Sens is not { } s) continue;
            if (s == Sens.Entree) entrees += c; else sorties += c;
        }

        var corps = (entrees, sorties) switch
        {
            (0, 0) => $"{Nombre(occurrences.Count, "événement")} cette semaine.",
            (0, _) => $"{Nombre(occurrences.Count, "mouvement")} · {Format.EurosSigne(sorties, Sens.Sortie)} à sortir.",
            (_, 0) => $"{Nombre(occurrences.Count, "mouvement")} · {Format.EurosSigne(entrees, Sens.Entree)} à venir.",
            _ => $"{Nombre(occurrences.Count, "mouvement")} · {Format.EurosSigne(entrees, Sens.Entree)} / {Format.EurosSigne(sorties, Sens.Sortie)}.",
        };

        return new Rappel(
            Cle: $"digest-{aujourdhui:yyyy-MM-dd}",
            Titre: "Le point de la semaine",
            Corps: corps);
    }

    /// <summary>Vrai le jour du digest — dimanche, comme convenu en D-018.</summary>
    public static bool EstJourDuDigest(DateTimeOffset maintenant) =>
        maintenant.LocalDateTime.DayOfWeek == DayOfWeek.Sunday;

    private static string Titre(OccurrenceCalendrier occurrence) => occurrence.Type switch
    {
        TypeElement.Facture or TypeElement.Paiement => "Échéance demain",
        TypeElement.Revenu => "Rentrée attendue demain",
        TypeElement.Rendezvous => "Rendez-vous demain",
        _ => "Demain",
    };

    private static string Corps(OccurrenceCalendrier occurrence)
    {
        var heure = occurrence.InstantUtc.ToLocalTime();
        var quand = heure.TimeOfDay == TimeSpan.Zero ? "" : $" · {heure:HH\\:mm}";

        return occurrence.MontantCentimes is { } centimes && occurrence.Sens is { } sens
            ? $"{occurrence.Titre} — {Format.EurosSigne(centimes, sens)}{quand}"
            : occurrence.Titre + quand;
    }

    private static string Nombre(int n, string mot) => n == 1 ? $"1 {mot}" : $"{n} {mot}s";
}
