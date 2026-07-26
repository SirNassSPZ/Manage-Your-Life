using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Sous-vue active de la zone Finances — un filtre sur la même liste, pas un autre écran.</summary>
public enum FiltreFinances { Tout, Entrees, Sorties, ParCategorie }

/// <summary>
/// Un groupe pliable de la vue « Par catégorie ». C'est de la <b>mise en forme sur des données déjà
/// là</b> : aucun champ, aucune entité, aucune règle de synchro en plus (I-004).
/// </summary>
public sealed partial class GroupeCategorie : ObservableObject
{
    /// <summary>Clé de pliage — l'identifiant de la catégorie, ou vide pour « Sans catégorie ».</summary>
    public required string Cle { get; init; }

    public required string Nom { get; init; }
    public required string Couleur { get; init; }
    public required IReadOnlyList<LigneFinance> Lignes { get; init; }

    /// <summary>Somme des lignes AFFICHÉES dans ce groupe — une étiquette de liste, pas une projection.</summary>
    public required string Total { get; init; }

    public required string Decompte { get; init; }

    /// <summary>Prévenu à chaque pliage, pour que la vue s'en souvienne d'un chargement à l'autre.</summary>
    public Action<GroupeCategorie>? AuBascule { get; init; }

    [ObservableProperty]
    private bool _deplie = true;

    /// <summary>
    /// La commande vit sur le groupe, pas sur le modèle de vue parent. C'est délibéré : liée depuis
    /// un gabarit, une <c>RelayCommand&lt;T&gt;</c> du parent reçoit le DataContext hérité tant que
    /// l'élément n'est pas posé, et lève dans <c>CanExecute</c>. Sans paramètre, le piège n'existe pas.
    /// </summary>
    [RelayCommand]
    private void Basculer()
    {
        Deplie = !Deplie;
        AuBascule?.Invoke(this);
    }
}

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

    /// <summary>Signé : positif pour une entrée, négatif pour une sortie. Sert aux sous-totaux par groupe.</summary>
    public required long CentimesSignes { get; init; }

    /// <summary>Les calendriers de l'Élément (§3.3) — une ligne peut en porter plusieurs, ou aucune.</summary>
    public required IReadOnlyList<Guid> Categories { get; init; }

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

/// <summary>
/// Une envie d'achat (§5.1). Depuis D-027 elle porte un <b>prix estimé facultatif</b> (§3.1) et
/// peut être confrontée au budget projeté (§5.1bis).
/// </summary>
public sealed record LigneEnvie(Guid Id, string Titre, string? Montant, long? Centimes, string Statut)
{
    /// <summary>Sans prix, il n'y a rien à confronter — le bouton ne doit pas s'afficher.</summary>
    public bool Confrontable => Centimes is > 0;
}

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

    /// <summary>Les mouvements du mois regroupés par catégorie, pour la sous-vue « Par catégorie ».</summary>
    public ObservableCollection<GroupeCategorie> Groupes { get; } = [];

    /// <summary>
    /// Ce que l'utilisateur a replié doit le RESTER. Les groupes sont reconstruits à chaque
    /// chargement ; sans cette mémoire, changer de mois rouvrirait tout ce qu'il vient de fermer —
    /// le même piège que les filtres de calendrier.
    /// </summary>
    private readonly HashSet<string> _replies = [];

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
    [NotifyPropertyChangedFor(nameof(MontrerVide))]
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

        var (occurrences, elements, envies, categories) = _composition.Acces.Lire(() =>
        (
            _composition.Calendrier.Occurrences(debut, fin),
            _composition.Lecture.Actifs().ToDictionary(e => e.Id),
            _composition.Lecture.Actifs(TypeElement.Envie),
            _composition.Lecture.Categories()
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
                CentimesSignes = sens == Sens.Entree ? centimes : -centimes,
                Categories = [.. element.Categories],
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

        ConstruireGroupes(categories);
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
                envie.Id,
                envie.Titre,
                envie.MontantCentimes is { } c ? Format.Euros(c) : null,
                envie.MontantCentimes,
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

    /// <summary>
    /// Regroupe les mouvements du mois par calendrier (§3.3). Une ligne sans catégorie tombe dans
    /// « Sans catégorie » — un groupe de plein droit, pas un oubli : c'est souvent le plus gros, et
    /// le voir est ce qui donne envie de ranger.
    /// <para>
    /// Un Élément peut porter <b>plusieurs</b> catégories : il apparaît alors dans chacune. Les
    /// sous-totaux ne s'additionnent donc pas au total du mois, et c'est assumé — ce sont des
    /// étiquettes de liste, pas des projections (règle 9).
    /// </para>
    /// </summary>
    private void ConstruireGroupes(IReadOnlyList<Categorie> categories)
    {
        Groupes.Clear();

        var lignes = Toutes().ToList();
        var connues = categories.ToDictionary(c => c.Id);

        // Les catégories d'abord, dans leur ordre de lecture ; « Sans catégorie » fermant la marche.
        foreach (var categorie in categories)
        {
            var duGroupe = lignes.Where(l => l.Categories.Contains(categorie.Id)).ToList();
            if (duGroupe.Count == 0) continue;

            Groupes.Add(Batir(
                categorie.Id.ToString(),
                categorie.Nom,
                string.IsNullOrWhiteSpace(categorie.Couleur) ? "#7A6AA6" : categorie.Couleur,
                duGroupe));
        }

        // Une catégorie mise à la corbeille disparaît de `categories` sans quitter les Éléments :
        // sans ce test, ses mouvements s'évaporeraient de la vue au lieu de retomber ici.
        var orphelines = lignes
            .Where(l => l.Categories.Count == 0 || !l.Categories.Any(connues.ContainsKey))
            .ToList();

        if (orphelines.Count > 0)
            Groupes.Add(Batir("", "Sans catégorie", "#9A938C", orphelines));
    }

    private GroupeCategorie Batir(string cle, string nom, string couleur, IReadOnlyList<LigneFinance> lignes)
    {
        var somme = lignes.Sum(l => l.CentimesSignes);
        return new GroupeCategorie
        {
            Cle = cle,
            Nom = nom,
            Couleur = couleur,
            Lignes = lignes,
            Total = Format.EurosRelatif(somme),
            Decompte = lignes.Count == 1 ? "1 mouvement" : $"{lignes.Count} mouvements",
            Deplie = !_replies.Contains(cle),
            AuBascule = Retenir,
        };
    }

    private void Retenir(GroupeCategorie groupe)
    {
        if (groupe.Deplie) _replies.Remove(groupe.Cle);
        else _replies.Add(groupe.Cle);
    }

    // ----- Confrontation au budget projeté (§5.1bis, V1 depuis D-027) -----

    /// <summary>Verdict de la dernière confrontation, prêt à lire. Null tant qu'on n'a rien demandé.</summary>
    [ObservableProperty]
    private string? _confrontation;

    /// <summary>Vrai quand ça passe — la vue s'en sert pour la couleur, pas pour le texte.</summary>
    [ObservableProperty]
    private bool _confrontationPasse;

    [ObservableProperty]
    private bool _confrontationEnCours;

    /// <summary>
    /// Confronte une envie au budget projeté, <b>sur le mois actuellement affiché</b> (§5.1bis).
    /// C'est ce qui évite un sélecteur de mois de plus : l'utilisateur navigue jusqu'à septembre,
    /// demande « ça rentre ? », et la réponse porte sur septembre.
    /// <para>
    /// Le calcul est fait par le SERVEUR (§4, règle 2) : rien n'est recalculé ici, et rien n'est
    /// écrit nulle part — une confrontation est une lecture (règle 9).
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task Confronter(LigneEnvie envie)
    {
        if (ConfrontationEnCours || envie.Centimes is not { } centimes || centimes <= 0) return;

        if (!_composition.Options.Api.EstConfiguree)
        {
            Confrontation = "Confrontation indisponible hors ligne : le calcul vit au serveur.";
            ConfrontationPasse = false;
            return;
        }

        ConfrontationEnCours = true;
        Confrontation = null;
        try
        {
            // L'horizon part du mois courant : il doit couvrir le mois visé, sinon le serveur
            // refuse une cible hors horizon — ce qui serait exact mais incompréhensible ici.
            var aujourdhui = DateOnly.FromDateTime(DateTime.Now);
            var ecart = ((_mois.Year - aujourdhui.Year) * 12) + _mois.Month - aujourdhui.Month;
            if (ecart < 0)
            {
                Confrontation = "Un mois passé ne se projette pas : choisis un mois à venir.";
                ConfrontationPasse = false;
                return;
            }

            var reponse = await _composition.Api.Confronter(
                centimes, _mois.ToString("yyyy-MM"), Math.Max(12, ecart + 1));

            ConfrontationPasse = reponse.Passe;
            Confrontation = reponse.Passe
                ? $"{envie.Titre} passe en {MoisAffiche.ToLowerInvariant()} — "
                  + $"il resterait {Reste(reponse)}."
                : $"{envie.Titre} ne passe pas : {Format.Euros(reponse.ManqueCentimes)} de trop "
                  + $"en {MoisLisible(reponse.PremierMoisQuiCasse)}.";
        }
        catch (Exception)
        {
            // Muet sur le détail : la vue Budget projeté est l'endroit qui explique un serveur
            // injoignable. Ici, on dit seulement que la réponse n'est pas venue.
            ConfrontationPasse = false;
            Confrontation = "Le serveur n'a pas répondu — réessaie dans un instant.";
        }
        finally { ConfrontationEnCours = false; }
    }

    /// <summary>Ce qu'il resterait à la clôture du mois visé, dans la projection simulée.</summary>
    private string Reste(App.Synchro.ReponseConfrontationClient reponse)
    {
        var cible = reponse.Simulee.FirstOrDefault(m => m.Mois == reponse.MoisCible);
        return cible?.ClotureCentimes is { } c ? Format.Euros(c) : "peu de marge";
    }

    /// <summary>« 2026-11 » → « novembre ». Un code de mois ne se lit pas dans une phrase.</summary>
    private static string MoisLisible(string? cle)
    {
        if (cle is null || !DateOnly.TryParse(cle + "-01", out var date)) return "un mois suivant";
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        return date.ToString("MMMM yyyy", fr);
    }

    private void AppliquerFiltre()
    {
        // Le filtre ne recharge rien : il masque une des deux listes. Les sous-vues « Entrées » et
        // « Sorties » de la maquette sont deux vues sur le même mois, pas deux requêtes.
        OnPropertyChanged(nameof(MontrerEntrees));
        OnPropertyChanged(nameof(MontrerSorties));
        OnPropertyChanged(nameof(MontrerGroupes));
        OnPropertyChanged(nameof(AucunGroupe));
        OnPropertyChanged(nameof(MontrerVide));
    }

    public bool MontrerEntrees => Filtre is FiltreFinances.Tout or FiltreFinances.Entrees;
    public bool MontrerSorties => Filtre is FiltreFinances.Tout or FiltreFinances.Sorties;
    public bool MontrerGroupes => Filtre is FiltreFinances.ParCategorie;
    public bool AucunGroupe => Groupes.Count == 0;

    /// <summary>
    /// L'état vide générique. La vue par catégorie a le sien, qui dit quoi faire : sans cette
    /// garde, un mois sans mouvement afficherait les deux messages l'un sous l'autre.
    /// </summary>
    public bool MontrerVide => ListeVide && !MontrerGroupes;

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
