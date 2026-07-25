using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Sous-vue active de la zone Finances — un filtre sur la même liste, pas un autre écran.</summary>
public enum FiltreFinances { Tout, Entrees, Sorties }

/// <summary>
/// Une ligne de mouvement du mois. <see cref="Selectionne"/> est la seule partie mutable : la
/// sélection multiple sert la confirmation en lot (D-017).
/// </summary>
public sealed partial class LigneFinance : ObservableObject
{
    public required Guid ElementId { get; init; }
    public required string Jour { get; init; }
    public required string MoisCourt { get; init; }
    public required string Titre { get; init; }
    public required string Montant { get; init; }
    public required bool EstEntree { get; init; }
    public required TypeElement Type { get; init; }

    /// <summary>« Mensuel » pour une série, null pour un ponctuel.</summary>
    public string? Recurrence { get; init; }

    /// <summary>« Payé », « À valider », « Reçu », « Attendu »… (§3.1).</summary>
    public required string Statut { get; init; }

    /// <summary>Vrai si l'Élément est déjà réglé — la pastille passe alors au vert.</summary>
    public required bool Regle { get; init; }

    /// <summary>
    /// Faux pour une série récurrente : un statut unique vaudrait pour TOUTES ses occurrences
    /// (D-017). La case est alors absente, et non pas présente puis refusée au clic.
    /// </summary>
    public required bool Confirmable { get; init; }

    [ObservableProperty]
    private bool _selectionne;
}

/// <summary>Une envie d'achat, simple liste de souhaits en V1 (§5.1 — la confronter au budget est V2).</summary>
public sealed record LigneEnvie(string Titre, string? Montant, string Statut);

/// <summary>
/// Finances (§5.1) — les mouvements du mois, la confirmation « payé/reçu », les envies d'achat.
/// <para>
/// Aucun calcul de budget ici (règle 9) : le chiffre de tête vient de <c>IClientApi.Projeter</c>,
/// calculé par le serveur. Les sous-totaux affichés à côté des groupes sont la somme des lignes
/// AFFICHÉES — une étiquette de liste, pas une projection.
/// </para>
/// </summary>
public sealed partial class VueModeleFinances : ObservableObject
{
    private readonly Composition _composition;
    private DateOnly _mois;

    public VueModeleFinances(Composition composition)
    {
        _composition = composition;
        var today = DateOnly.FromDateTime(DateTime.Now);
        _mois = new DateOnly(today.Year, today.Month, 1);
        Charger();
    }

    public ObservableCollection<LigneFinance> Entrees { get; } = [];
    public ObservableCollection<LigneFinance> Sorties { get; } = [];
    public ObservableCollection<LigneEnvie> Envies { get; } = [];

    [ObservableProperty]
    private string _moisAffiche = "";

    [ObservableProperty]
    private string _anneeAffichee = "";

    [ObservableProperty]
    private bool _horsMoisCourant;

    [ObservableProperty]
    private FiltreFinances _filtre = FiltreFinances.Tout;

    [ObservableProperty]
    private string _totalEntrees = "";

    [ObservableProperty]
    private string _totalSorties = "";

    /// <summary>« 1 payée · 5 à valider » — l'état d'avancement du mois, d'un coup d'œil.</summary>
    [ObservableProperty]
    private string _avancement = "";

    /// <summary>Chiffre de tête, calculé par le SERVEUR (§5.1). « — » tant qu'il n'a pas répondu.</summary>
    [ObservableProperty]
    private string _cloture = "—";

    [ObservableProperty]
    private bool _clotureConnue;

    [ObservableProperty]
    private int _nombreSelectionnes;

    [ObservableProperty]
    private bool _peutConfirmer;

    /// <summary>Retour de la dernière confirmation en lot — succès comme refus.</summary>
    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _listeVide;

    public bool AucuneEnvie => Envies.Count == 0;

    [RelayCommand]
    private void MoisPrecedent() { _mois = _mois.AddMonths(-1); Charger(); }

    [RelayCommand]
    private void MoisSuivant() { _mois = _mois.AddMonths(1); Charger(); }

    [RelayCommand]
    private void RevenirAujourdhui()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        _mois = new DateOnly(today.Year, today.Month, 1);
        Charger();
    }

    [RelayCommand]
    private void Filtrer(FiltreFinances filtre)
    {
        Filtre = filtre;
        Charger();
    }

    /// <summary>Bascule une ligne et met à jour le compteur de sélection.</summary>
    [RelayCommand]
    private void Basculer(LigneFinance ligne)
    {
        if (!ligne.Confirmable) return;
        ligne.Selectionne = !ligne.Selectionne;
        RecompterSelection();
    }

    /// <summary>
    /// Confirme en lot (D-017). Chaque Élément est jugé indépendamment côté cœur ; on rapporte
    /// le bilan sans jamais laisser un refus passer inaperçu.
    /// </summary>
    [RelayCommand]
    private void ConfirmerSelection()
    {
        var choisis = Toutes().Where(l => l.Selectionne && l.Confirmable).Select(l => l.ElementId).ToList();
        if (choisis.Count == 0) return;

        var bilan = _composition.Acces.Lire(() => _composition.Confirmation.ConfirmerLot(choisis));

        Message = (bilan.Confirmes.Count, bilan.Refuses.Count) switch
        {
            (0, 0) => null,
            ( > 0, 0) when bilan.Confirmes.Count == 1 => "1 mouvement confirmé.",
            ( > 0, 0) => $"{bilan.Confirmes.Count} mouvements confirmés.",
            // Le motif du cœur est déjà écrit pour l'utilisateur : on le relaie tel quel.
            (0, _) => bilan.Refuses[0].Motif,
            var (c, r) => $"{c} confirmé(s), {r} refusé(s) — {bilan.Refuses[0].Motif}",
        };

        Charger();
    }

    public void Charger()
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        MoisAffiche = fr.TextInfo.ToTitleCase(_mois.ToString("MMMM", fr));
        AnneeAffichee = _mois.ToString("yyyy", fr);

        var today = DateOnly.FromDateTime(DateTime.Now);
        HorsMoisCourant = _mois.Year != today.Year || _mois.Month != today.Month;

        var finMois = _mois.AddMonths(1).AddDays(-1);
        var debut = new DateTimeOffset(_mois.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fin = new DateTimeOffset(finMois.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var (occurrences, elements, envies) = _composition.Acces.Lire(() =>
        (
            _composition.Calendrier.Occurrences(debut, fin),
            _composition.Lecture.Actifs().ToDictionary(e => e.Id),
            _composition.Lecture.Actifs(TypeElement.Envie)
        ));

        Entrees.Clear();
        Sorties.Clear();

        long sommeEntrees = 0, sommeSorties = 0;
        int regles = 0, aValider = 0;

        foreach (var occurrence in occurrences)
        {
            if (occurrence.Sens is not { } sens || occurrence.MontantCentimes is not { } centimes) continue;
            if (!elements.TryGetValue(occurrence.ElementId, out var element)) continue;

            var recurrent = !string.IsNullOrWhiteSpace(element.Recurrence);
            var regle = element.Statut is StatutElement.Paye or StatutElement.Recu;
            var confirmable = !recurrent && element.Statut is StatutElement.AVenir or StatutElement.Attendu;

            var jourLocal = DateOnly.FromDateTime(occurrence.InstantUtc.ToLocalTime().DateTime);
            var ligne = new LigneFinance
            {
                ElementId = element.Id,
                Jour = jourLocal.Day.ToString(),
                MoisCourt = jourLocal.ToString("MMM", fr).TrimEnd('.'),
                Titre = element.Titre,
                Montant = Format.EurosSigne(centimes, sens),
                EstEntree = sens == Sens.Entree,
                Type = element.Type,
                Recurrence = recurrent ? LibelleRecurrence(element.Recurrence!) : null,
                Statut = Format.Statut(element.Statut),
                Regle = regle,
                Confirmable = confirmable,
            };

            if (sens == Sens.Entree) { sommeEntrees += centimes; Entrees.Add(ligne); }
            else { sommeSorties += centimes; Sorties.Add(ligne); }

            if (regle) regles++;
            else if (confirmable) aValider++;
        }

        AppliquerFiltre();

        TotalEntrees = Format.EurosSigne(sommeEntrees, Sens.Entree);
        TotalSorties = Format.EurosSigne(sommeSorties, Sens.Sortie);
        Avancement = (regles, aValider) switch
        {
            (0, 0) => "",
            (_, 0) => $"{regles} réglé(s)",
            (0, _) => $"{aValider} à valider",
            _ => $"{regles} réglé(s) · {aValider} à valider",
        };

        Envies.Clear();
        foreach (var envie in envies)
            Envies.Add(new LigneEnvie(
                envie.Titre,
                envie.MontantCentimes is { } c ? Format.Euros(c) : null,
                Format.Statut(envie.Statut)));
        OnPropertyChanged(nameof(AucuneEnvie));

        ListeVide = Entrees.Count == 0 && Sorties.Count == 0;
        RecompterSelection();
    }

    /// <summary>
    /// Le chiffre de tête vient du serveur (§5.1, règle 9). Appelé APRÈS l'affichage et jamais
    /// attendu : la liste du mois est locale et complète sans lui (filet 1).
    /// </summary>
    public async Task ChargerCloture()
    {
        ClotureConnue = false;
        Cloture = "—";
        if (!_composition.Options.Api.EstConfiguree) return;

        try
        {
            var projection = await _composition.Api.Projeter(12);
            var cle = _mois.ToString("yyyy-MM");
            var mois = projection.Mois.FirstOrDefault(m => m.Mois == cle && !m.AvantReference);
            if (mois?.ClotureCentimes is not { } centimes) return;

            Cloture = Format.EurosRelatif(centimes);
            ClotureConnue = true;
        }
        catch
        {
            // Muet : la vue Budget projeté est l'endroit qui explique un serveur injoignable.
        }
    }

    private void AppliquerFiltre()
    {
        // Le filtre ne recharge rien : il masque une des deux listes. Les sous-vues « Entrées » et
        // « Sorties » de la maquette sont deux vues sur le même mois, pas deux requêtes.
        OnPropertyChanged(nameof(MontrerEntrees));
        OnPropertyChanged(nameof(MontrerSorties));
    }

    public bool MontrerEntrees => Filtre is FiltreFinances.Tout or FiltreFinances.Entrees;
    public bool MontrerSorties => Filtre is FiltreFinances.Tout or FiltreFinances.Sorties;

    private IEnumerable<LigneFinance> Toutes() => Entrees.Concat(Sorties);

    private void RecompterSelection()
    {
        NombreSelectionnes = Toutes().Count(l => l.Selectionne);
        PeutConfirmer = NombreSelectionnes > 0;
    }

    /// <summary>RRULE → étiquette courte, pour dire « c'est une série » sans afficher la règle brute.</summary>
    private static string LibelleRecurrence(string rrule)
    {
        var r = rrule.ToUpperInvariant();
        if (r.Contains("FREQ=MONTHLY")) return "Mensuel";
        if (r.Contains("FREQ=WEEKLY")) return "Hebdo";
        if (r.Contains("FREQ=YEARLY")) return "Annuel";
        if (r.Contains("FREQ=DAILY")) return "Quotidien";
        return "Récurrent";
    }
}
