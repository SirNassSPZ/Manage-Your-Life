using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Une ligne d'agenda, déjà mise en forme pour l'affichage.</summary>
public sealed record LigneAgenda(
    string Heure,
    string? Mention,
    string Titre,
    string? Montant,
    TypeElement Type,
    Sens? Sens);

/// <summary>Un jour de l'agenda et ses mouvements.</summary>
public sealed class GroupeAgenda
{
    public required string Titre { get; init; }
    public required string Resume { get; init; }
    public required IReadOnlyList<LigneAgenda> Lignes { get; init; }
}

/// <summary>
/// Vue « Aujourd'hui » — on ouvre sur le calme (le solde et l'agenda du jour), pas sur la dette.
/// <para>
/// Ne calcule RIEN : tout vient de <see cref="ServiceAujourdhui"/>. Aucun service ne notifiant de
/// changement, la vue recharge explicitement après chaque écriture.
/// </para>
/// </summary>
public sealed partial class VueModeleAujourdhui : ObservableObject
{
    private readonly Composition _composition;

    public VueModeleAujourdhui(Composition composition) => _composition = composition;

    [ObservableProperty]
    private string _salutation = "Bonjour";

    [ObservableProperty]
    private string _dateDuJour = "";

    [ObservableProperty]
    private bool _soldePose;

    [ObservableProperty]
    private string _soldeAffiche = "—";

    [ObservableProperty]
    private string _soldeMention = "";

    [ObservableProperty]
    private int _nombreAujourdhui;

    [ObservableProperty]
    private int _nombreSeptJours;

    [ObservableProperty]
    private bool _agendaVide;

    /// <summary>« −180,00 € » — la clôture du premier mois à découvert, ou null s'il n'y en a pas.</summary>
    [ObservableProperty]
    private string? _alerteMontant;

    /// <summary>« en août 26 ».</summary>
    [ObservableProperty]
    private string? _alerteLibelle;

    public ObservableCollection<GroupeAgenda> Agenda { get; } = [];

    /// <summary>
    /// Le premier mois à découvert (§5.1 point 5 : « l'app met en évidence tout mois dont la clôture
    /// est négative »). La projection est SERVEUR (règle 9), donc cet appel part en tâche de fond :
    /// l'accueil s'affiche complet sans lui et ne l'attend jamais (filet 1).
    /// <para>
    /// Un échec est volontairement muet. L'accueil doit rester calme ; c'est la vue Budget projeté
    /// qui explique pourquoi la projection manque.
    /// </para>
    /// </summary>
    public async Task ChargerAlerteDecouvert()
    {
        AlerteMontant = null;
        AlerteLibelle = null;

        if (!_composition.Options.Api.EstConfiguree) return;

        try
        {
            var projection = await _composition.Api.Projeter(12);
            var decouvert = projection.Mois.FirstOrDefault(m =>
                !m.AvantReference && m.ClotureCentimes is < 0);

            if (decouvert is null) return;

            AlerteMontant = Format.EurosRelatif(decouvert.ClotureCentimes!.Value);
            AlerteLibelle = "en " + Format.MoisAbrege(decouvert.Mois);
        }
        catch
        {
            // Muet par choix : voir la remarque ci-dessus.
        }
    }

    public void Charger()
    {
        var maintenant = DateTimeOffset.Now;
        var aujourdhui = DateOnly.FromDateTime(maintenant.LocalDateTime);

        DateDuJour = Format.JourLong(aujourdhui);
        Salutation = maintenant.Hour < 18 ? "Bonjour" : "Bonsoir";

        var (solde, duJour, prochains) = _composition.Acces.Lire(() =>
        (
            _composition.Aujourdhui.SoldeDeReference(),
            _composition.Aujourdhui.Aujourdhui(maintenant),
            _composition.Aujourdhui.ProchainsJours(maintenant, 7)
        ));

        SoldePose = solde is not null;
        if (solde is not null)
        {
            SoldeAffiche = Format.Euros(solde.Centimes);
            SoldeMention = solde.Date == aujourdhui
                ? "Recalé aujourd'hui. Factures, revenus et envies se projettent à partir de ce point."
                : $"Recalé le {Format.JourLong(solde.Date)}. Factures, revenus et envies se projettent à partir de ce point.";
        }
        else
        {
            SoldeAffiche = "—";
            SoldeMention = "Pose ton solde de référence : sans point de départ, aucune projection n'est possible.";
        }

        NombreAujourdhui = duJour.Count;
        NombreSeptJours = prochains.Sum(j => j.Occurrences.Count);

        Agenda.Clear();
        foreach (var jour in prochains)
            Agenda.Add(Grouper(jour, aujourdhui));

        AgendaVide = Agenda.Count == 0;
    }

    private static GroupeAgenda Grouper(JourAgenda jour, DateOnly aujourdhui)
    {
        var ecart = jour.Jour.DayNumber - aujourdhui.DayNumber;
        var intitule = ecart switch
        {
            0 => $"Aujourd'hui · {Format.JourCourt(jour.Jour)}",
            1 => $"Demain · {Format.JourCourt(jour.Jour)}",
            _ => Format.JourLong(jour.Jour),
        };

        var n = jour.Occurrences.Count;
        return new GroupeAgenda
        {
            // Capitales posées ici : WinUI n'a pas de « text-transform » (cf. Format.Capitales).
            Titre = Format.Capitales(intitule),
            Resume = n == 1 ? "1 mouvement" : $"{n} mouvements",
            Lignes = [.. jour.Occurrences.Select(Ligne)],
        };
    }

    private static LigneAgenda Ligne(OccurrenceCalendrier occurrence)
    {
        var sansHeure = occurrence.InstantUtc.ToLocalTime().TimeOfDay == TimeSpan.Zero;
        return new LigneAgenda(
            Heure: sansHeure ? "Prévu" : occurrence.InstantUtc.ToLocalTime().ToString("HH:mm"),
            Mention: sansHeure ? "jour" : null,
            Titre: occurrence.Titre,
            Montant: occurrence.MontantCentimes is { } c && occurrence.Sens is { } s
                ? Format.EurosSigne(c, s)
                : null,
            Type: occurrence.Type,
            Sens: occurrence.Sens);
    }
}
