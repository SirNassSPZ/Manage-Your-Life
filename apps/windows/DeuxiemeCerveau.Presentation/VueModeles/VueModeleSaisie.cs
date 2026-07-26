using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Une catégorie cochable dans le formulaire de saisie (§3.3).</summary>
public sealed partial class ChoixCategorie : ObservableObject
{
    public required Guid Id { get; init; }
    public required string Nom { get; init; }
    public required string Couleur { get; init; }

    [ObservableProperty]
    private bool _choisie;
}

/// <summary>Périodicité proposée. Traduite en RRULE (règle 6) — jamais de format maison.</summary>
public enum Periodicite { Aucune, Mensuel, Hebdomadaire, Annuel }

/// <summary>
/// Un type saisissable, avec son état d'activation. Un objet plutôt qu'un simple
/// <see cref="TypeElement"/> pour une raison d'interface : sans <see cref="Actif"/>, les boutons de
/// type sont indiscernables et l'utilisateur ne voit pas ce qu'il est en train de créer.
/// </summary>
public sealed partial class ChoixType : ObservableObject
{
    public required TypeElement Type { get; init; }
    public required string Nom { get; init; }

    [ObservableProperty]
    private bool _actif;
}

/// <summary>
/// Saisie typée (§13, V1) : facture, paiement, revenu, rendez-vous, envie. La note a son propre
/// écran (§5.5), la tâche appartient aux projets (§5.3).
/// <para>
/// <b>Filet 1 appliqué à la lettre</b> : l'enregistrement écrit dans la base locale et rien
/// d'autre. Aucun appel réseau ici — la synchro repassera derrière (§6).
/// </para>
/// </summary>
public sealed partial class VueModeleSaisie : ObservableObject
{
    private readonly Composition _composition;

    public VueModeleSaisie(Composition composition)
    {
        _composition = composition;
        _date = DateOnly.FromDateTime(DateTime.Now);
    }

    /// <summary>Rappelé après un enregistrement réussi : toutes les vues en dépendent.</summary>
    public Action? ApresEnregistrement { get; set; }

    public ObservableCollection<ChoixCategorie> Categories { get; } = [];

    /// <summary>
    /// Les types offerts, <b>selon l'endroit d'où l'on ouvre le formulaire</b>. Un rendez-vous se
    /// plane depuis le Calendrier, pas depuis Finances : proposer l'argent et l'agenda dans le même
    /// menu partout brouille ce que chaque onglet sert à faire.
    /// </summary>
    public ObservableCollection<ChoixType> Types { get; } = [];

    private static readonly (TypeElement Type, string Nom)[] Catalogue =
    [
        (TypeElement.Facture, "Facture"),
        (TypeElement.Paiement, "Paiement"),
        (TypeElement.Revenu, "Revenu"),
        (TypeElement.Envie, "Envie"),
        (TypeElement.Rendezvous, "Rendez-vous"),
    ];

    /// <summary>L'argent et les envies : ce que la zone Finances sait recevoir (§5.1).</summary>
    private static readonly TypeElement[] PourFinances =
        [TypeElement.Facture, TypeElement.Paiement, TypeElement.Revenu, TypeElement.Envie];

    /// <summary>Le calendrier plane les rendez-vous, et rien d'autre (§5.4).</summary>
    private static readonly TypeElement[] PourCalendrier = [TypeElement.Rendezvous];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DemandeMontant))]
    [NotifyPropertyChangedFor(nameof(DemandeDate))]
    [NotifyPropertyChangedFor(nameof(MontantObligatoire))]
    [NotifyPropertyChangedFor(nameof(LibelleMontant))]
    private TypeElement _type = TypeElement.Facture;

    [ObservableProperty]
    private string _titre = "";

    [ObservableProperty]
    private string _montant = "";

    [ObservableProperty]
    private DateOnly _date;

    [ObservableProperty]
    private Periodicite _periodicite = Periodicite.Aucune;

    [ObservableProperty]
    private string? _erreur;

    [ObservableProperty]
    private bool _ouvert;

    /// <summary>
    /// L'Élément en cours de correction, ou <c>null</c> si l'on crée. C'est le seul état qui
    /// distingue les deux modes — le formulaire, lui, est rigoureusement le même.
    /// </summary>
    private Guid? _idEnEdition;

    /// <summary>Statut de l'Élément corrigé, à préserver : corriger un libellé ne « dépaie » pas une facture.</summary>
    private StatutElement _statutEnEdition;

    /// <summary>
    /// La RRULE d'origine de l'Élément corrigé. Conservée pour ne pas <b>effacer en silence</b> une
    /// récurrence que ces trois boutons ne savent pas représenter (D-003 en accepte bien
    /// davantage : « tous les 2 mois », « dernier jour du mois »…).
    /// </summary>
    private string? _recurrenceOrigine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitreFormulaire))]
    [NotifyPropertyChangedFor(nameof(LibelleAction))]
    private bool _estEdition;

    public string TitreFormulaire => EstEdition ? "Modifier" : "Ajouter";

    public string LibelleAction => EstEdition ? "Enregistrer" : "Ajouter";

    /// <summary>Une envie porte un prix ESTIMÉ et facultatif (§3.1, D-027) ; le rendez-vous, rien.</summary>
    public bool DemandeMontant => Type is not TypeElement.Rendezvous;

    /// <summary>Une envie n'a pas de date arrêtée : c'est un souhait, pas une échéance.</summary>
    public bool DemandeDate => Type is not TypeElement.Envie;

    public bool MontantObligatoire => Type is TypeElement.Facture or TypeElement.Paiement or TypeElement.Revenu;

    public string LibelleMontant => Type == TypeElement.Envie ? "Prix estimé (facultatif)" : "Montant";

    /// <summary>Ouvre le formulaire pour la zone Finances : argent et envies.</summary>
    [RelayCommand]
    private void OuvrirFinances() => Ouvrir(PourFinances);

    /// <summary>Ouvre le formulaire pour la zone Calendrier : rendez-vous.</summary>
    [RelayCommand]
    private void OuvrirCalendrier() => Ouvrir(PourCalendrier);

    /// <summary>Ouvre le formulaire et recharge les catégories cochables.</summary>
    public void Ouvrir(IReadOnlyList<TypeElement>? types = null)
    {
        var offerts = types ?? [.. Catalogue.Select(c => c.Type)];
        Types.Clear();
        foreach (var (type, nom) in Catalogue.Where(c => offerts.Contains(c.Type)))
            Types.Add(new ChoixType { Type = type, Nom = nom });

        // Le premier type offert devient le type actif : sans ça, le formulaire s'ouvrirait sur un
        // type absent de ses propres boutons.
        Type = Types[0].Type;
        Types[0].Actif = true;

        var categories = _composition.Acces.Lire(() => _composition.Lecture.Categories());
        Categories.Clear();
        foreach (var categorie in categories)
            Categories.Add(new ChoixCategorie
            {
                Id = categorie.Id,
                Nom = categorie.Nom,
                Couleur = string.IsNullOrWhiteSpace(categorie.Couleur) ? "#7A6AA6" : categorie.Couleur,
            });

        Erreur = null;
        Ouvert = true;
    }

    /// <summary>
    /// Ouvre le formulaire sur un Élément <b>existant</b>, pour le corriger (§5 — « tout ce qui est
    /// affiché se modifie »). Jusqu'ici un Élément était figé dès sa création : la seule façon de
    /// rattraper une faute de frappe était de le supprimer et de le ressaisir.
    /// <para>
    /// Tous les types sont offerts, y compris pour une correction : se tromper de type à la saisie
    /// est justement l'erreur qu'on veut pouvoir réparer.
    /// </para>
    /// </summary>
    public void OuvrirPour(Element element)
    {
        Ouvrir();

        _idEnEdition = element.Id;
        _statutEnEdition = element.Statut;
        _recurrenceOrigine = element.Recurrence;
        EstEdition = true;

        Type = element.Type;
        foreach (var choix in Types) choix.Actif = choix.Type == element.Type;

        Titre = element.Titre;
        Montant = element.MontantCentimes is { } c ? Format.Euros(c).Replace(" €", "").Trim() : "";
        Date = element.DateDebut is { } d
            ? DateOnly.FromDateTime(d.ToLocalTime().DateTime)
            : DateOnly.FromDateTime(DateTime.Now);
        Periodicite = DepuisRrule(element.Recurrence);

        foreach (var choix in Categories)
            choix.Choisie = element.Categories.Contains(choix.Id);
    }

    /// <summary>
    /// Met l'Élément corrigé à la corbeille (filet 2 — marqué, jamais détruit ; §5.6). N'a de sens
    /// qu'en édition : il n'y a rien à supprimer d'un Élément qui n'existe pas encore.
    /// </summary>
    [RelayCommand]
    private void Supprimer()
    {
        if (_idEnEdition is not { } id) return;

        var resultat = _composition.Acces.Lire(
            () => _composition.Saisie.Supprimer(EntiteSynchro.Element, id));

        if (!resultat.Reussi)
        {
            Erreur = string.Join(" / ", resultat.Erreurs.Select(e => e.Message));
            return;
        }

        Ouvert = false;
        Reinitialiser();
        ApresEnregistrement?.Invoke();
    }

    [RelayCommand]
    private void Fermer()
    {
        Ouvert = false;
        Reinitialiser();
    }

    [RelayCommand]
    private void ChoisirType(ChoixType choix)
    {
        Type = choix.Type;
        foreach (var autre in Types) autre.Actif = ReferenceEquals(autre, choix);
    }

    [RelayCommand]
    private void BasculerCategorie(ChoixCategorie choix) => choix.Choisie = !choix.Choisie;

    [RelayCommand]
    private void Enregistrer()
    {
        var titre = Titre.Trim();
        if (titre.Length == 0)
        {
            Erreur = "Un titre, même court, est nécessaire.";
            return;
        }

        long? centimes = null;
        if (DemandeMontant && Montant.Trim().Length > 0)
        {
            // TryCentimes est la porte d'entrée de la règle 5 : jamais de flottant pour l'argent.
            if (!Format.TryCentimes(Montant, out var lu))
            {
                Erreur = $"Montant illisible : « {Montant.Trim()} ».";
                return;
            }
            centimes = lu;
        }

        if (MontantObligatoire && centimes is null)
        {
            Erreur = "Un montant est obligatoire pour une facture, un paiement ou un revenu.";
            return;
        }

        // En correction, on repart de l'entité STOCKÉE : un objet neuf écraserait ses champs
        // d'audit (§3.1) — date de création, version, appareil source — et la synchro perdrait le
        // fil de son histoire. Même précaution que pour les notes.
        Element element;
        if (_idEnEdition is { } id)
        {
            var etat = _composition.Acces.Lire(() => _composition.Depot.Obtenir(EntiteSynchro.Element, id));
            if (etat is null)
            {
                Erreur = "Cet élément n'existe plus — il a peut-être été supprimé ailleurs.";
                return;
            }

            element = Core.Json.SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);

            // Le statut se garde tel quel : corriger un libellé ne doit pas « dépayer » une facture.
            // Il n'est refait que si le TYPE change, car la table des statuts est propre à chaque
            // type (§3.1) et l'ancien deviendrait invalide.
            element.Statut = element.Type == Type ? _statutEnEdition : StatutParDefaut(Type);
            element.Type = Type;

            // Repartir à blanc sur ce que le formulaire pilote : sans ça, retirer une date ou une
            // récurrence serait impossible — l'ancienne valeur survivrait.
            element.DateDebut = null;
            element.Fuseau = null;
            element.Recurrence = null;
            element.Categories.Clear();
        }
        else
        {
            element = new Element { Statut = StatutParDefaut(Type), Type = Type };
        }

        Appliquer(element, titre, centimes);

        var resultat = _composition.Acces.Lire(
            () => _composition.Saisie.Enregistrer(element, EntiteSynchro.Element));

        if (!resultat.Reussi)
        {
            // Les motifs du cœur sont déjà écrits pour l'utilisateur : on les relaie tels quels.
            Erreur = string.Join(" / ", resultat.Erreurs.Select(e => e.Message));
            return;
        }

        Ouvert = false;
        Reinitialiser();
        ApresEnregistrement?.Invoke();
    }

    /// <summary>
    /// Pose sur l'Élément ce que le formulaire pilote — création et correction confondues.
    /// <b>Un seul endroit</b> : deux chemins d'écriture divergeraient au premier champ ajouté.
    /// </summary>
    private void Appliquer(Element element, string titre, long? centimes)
    {
        element.Titre = titre;
        element.MontantCentimes = centimes;

        // La devise n'accompagne QUE le montant : posée seule, le cœur la refuse.
        element.Devise = centimes is null ? null : "EUR";

        element.Sens = Type switch
        {
            TypeElement.Revenu => Core.Modele.Sens.Entree,
            TypeElement.Facture or TypeElement.Paiement => Core.Modele.Sens.Sortie,
            // Ni le rendez-vous ni l'envie ne portent de sens (§3.1).
            _ => null,
        };

        if (DemandeDate)
        {
            element.DateDebut = new DateTimeOffset(Date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
            element.Fuseau = "Europe/Paris";

            // Une récurrence trop riche pour ces trois boutons est CONSERVÉE tant que l'utilisateur
            // ne touche pas au choix de périodicité. Sans ça, corriger le titre d'un « dernier jour
            // du mois » l'effacerait sans rien dire. Choisir explicitement une périodicité la
            // remplace — c'est alors un geste voulu.
            element.Recurrence = _recurrenceOrigine is { Length: > 0 } origine
                                 && Periodicite == DepuisRrule(origine)
                ? origine
                : Rrule(Periodicite);
        }

        foreach (var choix in Categories.Where(c => c.Choisie))
            element.Categories.Add(choix.Id);
    }

    private void Reinitialiser()
    {
        Titre = "";
        Montant = "";
        Periodicite = Periodicite.Aucune;
        Date = DateOnly.FromDateTime(DateTime.Now);
        Erreur = null;
        _idEnEdition = null;
        _recurrenceOrigine = null;
        EstEdition = false;
        foreach (var choix in Categories) choix.Choisie = false;
    }

    /// <summary>Statut d'ouverture, par type (§3.1 — la table fait foi).</summary>
    private static StatutElement StatutParDefaut(TypeElement type) => type switch
    {
        TypeElement.Revenu => StatutElement.Attendu,
        TypeElement.Rendezvous => StatutElement.Planifie,
        TypeElement.Envie => StatutElement.Idee,
        _ => StatutElement.AVenir,
    };

    /// <summary>
    /// Périodicité → RRULE (RFC 5545, règle 6). Le cœur développe la règle dans le fuseau de
    /// l'Élément : « le 5 » reste le 5, changements d'heure compris.
    /// </summary>
    public static string? Rrule(Periodicite periodicite) => periodicite switch
    {
        Periodicite.Mensuel => "FREQ=MONTHLY",
        Periodicite.Hebdomadaire => "FREQ=WEEKLY",
        Periodicite.Annuel => "FREQ=YEARLY",
        _ => null,
    };

    /// <summary>
    /// RRULE → périodicité, pour préremplir le formulaire en correction. Le cœur accepte un
    /// sous-ensemble bien plus large de RFC 5545 que ces trois cas (D-003) : une règle que ce
    /// formulaire ne sait pas représenter retombe sur « Aucune » à l'affichage, et serait donc
    /// <b>écrasée</b> si l'utilisateur enregistrait. D'où le refus de la corriger ici — voir
    /// <see cref="RecurrenceTropRiche"/>.
    /// </summary>
    public static Periodicite DepuisRrule(string? rrule) => rrule switch
    {
        "FREQ=MONTHLY" => Periodicite.Mensuel,
        "FREQ=WEEKLY" => Periodicite.Hebdomadaire,
        "FREQ=YEARLY" => Periodicite.Annuel,
        _ => Periodicite.Aucune,
    };

    /// <summary>
    /// Vrai si l'Élément porte une récurrence que ce formulaire ne sait pas représenter (« tous les
    /// 2 mois », « dernier jour du mois »…). L'ouvrir en correction l'afficherait « Aucune » et
    /// l'effacerait au premier enregistrement, <b>en silence</b>.
    /// </summary>
    public static bool RecurrenceTropRiche(Element element) =>
        element.Recurrence is { Length: > 0 } r && DepuisRrule(r) == Periodicite.Aucune;
}
