using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Rappelé après un recalage : tout l'écran repose sur le solde de référence.</summary>
    public Action? ApresChangement { get; set; }

    // ----- Recalage du solde de référence (§3.4) -----
    //
    // Le bouton « Recaler le solde » existait dans la vue depuis l'Étape 4f mais n'était relié à
    // RIEN, alors que ServiceRecalage était écrit et testé. C'est le geste UNIQUE de correction
    // prévu par la spec : on redonne son solde réel et tout se reprojette à partir de là.

    [ObservableProperty]
    private bool _recalageOuvert;

    [ObservableProperty]
    private string _soldeSaisi = "";

    [ObservableProperty]
    private DateOnly _dateRecalage = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>L'écart affiché TEL QUEL avant validation — pour que le geste soit compris, pas subi.</summary>
    [ObservableProperty]
    private string? _apercuRecalage;

    [ObservableProperty]
    private string? _erreurRecalage;

    [RelayCommand]
    private void OuvrirRecalage()
    {
        var actuel = _composition.Acces.Lire(() => _composition.Aujourdhui.SoldeDeReference());
        // Pré-remplir avec le solde actuel : on corrige un écart, on ne repart pas de zéro.
        SoldeSaisi = actuel is null ? "" : Format.Euros(actuel.Centimes).Replace(" €", "").Trim();
        DateRecalage = DateOnly.FromDateTime(DateTime.Now);
        ApercuRecalage = null;
        ErreurRecalage = null;
        RecalageOuvert = true;
    }

    [RelayCommand]
    private void FermerRecalage()
    {
        RecalageOuvert = false;
        ApercuRecalage = null;
        ErreurRecalage = null;
    }

    /// <summary>
    /// Aperçu sans rien écrire : d'où l'on part, où l'on va, et l'écart. C'est ce que
    /// <c>ServiceRecalage.Preparer</c> existe pour donner.
    /// </summary>
    [RelayCommand]
    private void ApercuRecalageCommande()
    {
        if (!LireSoldeSaisi(out var centimes)) return;

        var etat = _composition.Acces.Lire(() => _composition.Recalage.Preparer(centimes, DateRecalage));
        ApercuRecalage = etat.EcartCentimes is not { } ecart
            ? $"Premier réglage : le solde de départ sera {Format.Euros(etat.NouveauCentimes)}."
            : ecart == 0
                ? "Aucun écart : la projection part déjà du bon endroit."
                : $"Écart de {Format.EurosRelatif(ecart)} par rapport au {Format.JourLong(etat.AncienneDate!.Value)}. "
                  + "Tout se reprojettera à partir du nouveau point.";
    }

    [RelayCommand]
    private void AppliquerRecalage()
    {
        if (!LireSoldeSaisi(out var centimes)) return;

        var resultat = _composition.Acces.Lire(() => _composition.Recalage.Appliquer(centimes, DateRecalage));
        if (!resultat.Reussi)
        {
            ErreurRecalage = string.Join(" / ", resultat.Erreurs.Select(e => e.Message));
            return;
        }

        RecalageOuvert = false;
        ApercuRecalage = null;
        Charger();
        ApresChangement?.Invoke();
    }

    /// <summary>Un découvert se saisit avec un signe moins (§3.4) : le solde négatif est autorisé.</summary>
    private bool LireSoldeSaisi(out long centimes)
    {
        ErreurRecalage = null;
        if (Format.TryCentimes(SoldeSaisi, out centimes)) return true;
        ErreurRecalage = $"Montant illisible : « {SoldeSaisi.Trim()} ».";
        return false;
    }

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
    /// Va chercher au serveur ce que l'accueil ne peut pas calculer : le <b>solde courant</b>
    /// (§5.1) et le premier mois à découvert (§5.1 point 5).
    /// <para>
    /// Les deux viennent du même appel, à dessein : c'est le même état lu au même instant. Deux
    /// appels séparés pourraient répondre sur deux lectures différentes et afficher un solde qui
    /// contredit son alerte.
    /// </para>
    /// <para>
    /// La projection est SERVEUR (règle 9), donc cet appel part en tâche de fond : l'accueil
    /// s'affiche complet sans lui et ne l'attend jamais (filet 1).
    /// </para>
    /// <para>
    /// <b>Un échec ne laisse pas un chiffre faux à l'écran.</b> L'alerte s'efface en silence — c'est
    /// la vue Budget projeté qui explique pourquoi la projection manque — mais le solde, lui, dit
    /// franchement qu'il est indisponible. Afficher le solde de référence à sa place ferait
    /// exactement ce qu'on cherche à corriger : montrer un nombre immobile qu'on prend pour son
    /// argent du moment.
    /// </para>
    /// </summary>
    public async Task ChargerAlerteDecouvert()
    {
        AlerteMontant = null;
        AlerteLibelle = null;

        if (!SoldePose) return; // sans point de départ, il n'y a rien à projeter (§3.4)

        if (!_composition.Options.Api.EstConfiguree)
        {
            SoldeIndisponible();
            return;
        }

        try
        {
            var projection = await _composition.Api.Projeter(12);

            SoldeAffiche = Format.Euros(projection.SoldeCourantCentimes);
            SoldeMention = _mentionReference;

            var decouvert = projection.Mois.FirstOrDefault(m =>
                !m.AvantReference && m.ClotureCentimes is < 0);

            if (decouvert is null) return;

            AlerteMontant = Format.EurosRelatif(decouvert.ClotureCentimes!.Value);
            AlerteLibelle = "en " + Format.MoisAbrege(decouvert.Mois);
        }
        catch
        {
            SoldeIndisponible();
        }
    }

    private void SoldeIndisponible()
    {
        SoldeAffiche = "—";
        SoldeMention = "Solde indisponible : il est calculé par le serveur. "
            + _mentionReference;
    }

    /// <summary>D'où part la projection — la vraie place du solde de référence (§3.4).</summary>
    private string _mentionReference = "";

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
            // Le grand chiffre est le SOLDE COURANT, qui vient du serveur (§5.1) — pas le solde de
            // référence, immobile par conception (§3.4). C'est cette confusion qui faisait lire
            // « 700 € » comme un montant bloqué dont on ne savait pas ce qu'il représentait.
            // En attendant la réponse : un tiret, jamais une valeur d'attente qu'on prendrait
            // pour le résultat.
            var quand = solde.Date == aujourdhui ? "aujourd'hui" : "le " + Format.JourLong(solde.Date);
            _mentionReference = $"Point de départ : {Format.Euros(solde.Centimes)}, posé {quand}.";

            SoldeAffiche = "—";
            SoldeMention = _mentionReference;
        }
        else
        {
            _mentionReference = "";
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
