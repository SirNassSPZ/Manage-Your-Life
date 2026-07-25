using System.Globalization;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Windows.Presentation;

/// <summary>
/// Mise en forme pour l'affichage seulement. L'argent reste en centimes entiers partout ailleurs
/// (règle 5) : la division par 100 n'existe qu'ici, au dernier moment.
/// </summary>
public static class Format
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>« 2 483,60 € ». Espace insécable fine avant le symbole, comme la maquette.</summary>
    public static string Euros(long centimes) =>
        (centimes / 100m).ToString("N2", Fr) + " €";

    /// <summary>« +2 380,00 € » / « −840,00 € ». Le signe moins typographique, pas le trait d'union.</summary>
    public static string EurosSigne(long centimes, Sens sens)
    {
        var montant = Euros(Math.Abs(centimes));
        return sens == Sens.Entree ? "+" + montant : "−" + montant;
    }

    /// <summary>Montant signé d'après le solde lui-même (projection : une clôture peut être négative).</summary>
    public static string EurosRelatif(long centimes) =>
        centimes < 0 ? "−" + Euros(Math.Abs(centimes)) : Euros(centimes);

    /// <summary>« jeudi 24 juillet ».</summary>
    public static string JourLong(DateOnly jour) => jour.ToString("dddd d MMMM", Fr);

    /// <summary>« jeu. 24 ».</summary>
    public static string JourCourt(DateOnly jour) => jour.ToString("ddd d", Fr);

    /// <summary>« Juillet 2026 ».</summary>
    public static string MoisAnnee(DateOnly jour) =>
        Fr.TextInfo.ToTitleCase(jour.ToString("MMMM yyyy", Fr));

    /// <summary>« 18:00 » pour un horaire, « Prévu » pour une échéance sans heure.</summary>
    public static string Heure(DateTimeOffset instant, bool journeeEntiere) =>
        journeeEntiere ? "Prévu" : instant.ToLocalTime().ToString("HH:mm", Fr);

    /// <summary>Étiquette de statut affichée en pastille, d'après la table §3.1.</summary>
    public static string Statut(StatutElement statut) => statut switch
    {
        StatutElement.AVenir => "À valider",
        StatutElement.Paye => "Payé",
        StatutElement.Attendu => "Attendu",
        StatutElement.Recu => "Reçu",
        StatutElement.Annule => "Annulé",
        StatutElement.AFaire => "À faire",
        StatutElement.Fait => "Fait",
        StatutElement.Reporte => "Reporté",
        StatutElement.Planifie => "Planifié",
        StatutElement.Idee => "Idée",
        StatutElement.Planifiee => "Planifiée",
        StatutElement.Faite => "Faite",
        StatutElement.Abandonnee => "Abandonnée",
        StatutElement.Active => "Active",
        StatutElement.Archivee => "Archivée",
        _ => statut.ToString(),
    };
}
