using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>
/// Une tâche de projet (§5.3) telle que la liste l'affiche.
/// <para>
/// <see cref="Faite"/> et <see cref="Priorite"/> sont <b>mutables et observables</b> : cocher une
/// tâche modifie la ligne SUR PLACE au lieu de reconstruire toute la liste. Reconstruire faisait
/// recréer les lignes sous le clic, et les coches se retrouvaient sur les mauvaises.
/// </para>
/// </summary>
public sealed partial class LigneTache : ObservableObject
{
    public required Guid Id { get; init; }
    public required string Titre { get; init; }
    public required bool Reportee { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EtiquettePriorite))]
    [NotifyPropertyChangedFor(nameof(APriorite))]
    private Priorite _priorite;

    [ObservableProperty]
    private bool _faite;

    /// <summary>« Haute », « Basse » — vide pour la priorité normale, qui n'a rien à annoncer.</summary>
    public string EtiquettePriorite => Priorite switch
    {
        Priorite.Haute => "Haute",
        Priorite.Basse => "Basse",
        _ => "",
    };

    public bool APriorite => EtiquettePriorite.Length > 0;

    /// <summary>
    /// Renotifie la coche sans changer sa valeur. Une CheckBox se coche d'elle-même au clic : si
    /// l'écriture échoue, il faut la ramener sur l'état réel, sinon elle affiche un mensonge.
    /// </summary>
    internal void RemettreCocheAuReel() => OnPropertyChanged(nameof(Faite));
}

/// <summary>Un projet et ses tâches (§3.2, §5.3).</summary>
public sealed partial class LigneProjet : ObservableObject
{
    public required Guid Id { get; init; }
    public required string Nom { get; init; }
    public required string Couleur { get; init; }
    public required StatutProjet Statut { get; init; }
    public required IReadOnlyList<LigneTache> Taches { get; init; }

    /// <summary>
    /// « 3 tâches · 1 faite » — l'avancement d'un coup d'œil. Observable : cocher une tâche le met
    /// à jour sans reconstruire la liste.
    /// </summary>
    [ObservableProperty]
    private string _avancement = "";

    public bool EstActif => Statut == StatutProjet.Actif;

    /// <summary>« En pause », « Terminé » — vide quand le projet est actif.</summary>
    public string EtiquetteStatut => Statut switch
    {
        StatutProjet.EnPause => "En pause",
        StatutProjet.Termine => "Terminé",
        _ => "",
    };

    [ObservableProperty]
    private bool _selectionne;
}

/// <summary>
/// Projets personnels (§5.3, entrés en V1 par D-027) : objectifs de vie, leurs tâches propres, et
/// leur calendrier qui devient automatiquement un filtre du calendrier principal (§5.4).
/// <para>
/// <b>Un projet crée sa catégorie.</b> C'est ce que veut dire « calendrier dédié automatique » :
/// la catégorie porte <c>Origine = Projet</c>, ce qui la distingue d'une catégorie créée à la main
/// et permet au calendrier de la présenter à part sans qu'aucune liste ne soit tenue en double.
/// </para>
/// <para>
/// <b>Et ce qui naît ensemble vit ensemble</b> (§5.3, D-030) : renommer ou recolorier le projet
/// renomme et recolorie son calendrier, le mettre à la corbeille y met aussi son calendrier. Seule
/// la <b>fermeture</b> fait exception, et c'est voulu — un projet terminé existe toujours, son
/// calendrier reste, simplement éteint par défaut.
/// </para>
/// </summary>
public sealed partial class VueModeleProjets : ObservableObject
{
    private readonly Composition _composition;

    public VueModeleProjets(Composition composition)
    {
        _composition = composition;
        RattraperCalendriersOrphelins();
        Charger();
    }

    /// <summary>Rappelé après toute écriture : la barre latérale et le calendrier en dépendent.</summary>
    public Action? ApresChangement { get; set; }

    /// <summary>
    /// Rappelé après une écriture que cette vue a déjà reflétée sur place. Ne doit RIEN recharger
    /// ici — sinon la liste se reconstruit sous le clic et les coches partent de travers.
    /// </summary>
    public Action? ApresChangementLeger { get; set; }

    public ObservableCollection<LigneProjet> Projets { get; } = [];

    /// <summary>
    /// Le calendrier du projet ouvert (§5.3, D-028) : les mêmes occurrences que le calendrier
    /// principal, mais filtrées sur le seul calendrier de ce projet.
    /// <para>
    /// Le filtre du calendrier principal répond à « qu'est-ce qui arrive cette semaine, tous sujets
    /// confondus ». Il ne répond pas à « où en est ce projet dans le temps », qui demande de ne voir
    /// que lui.
    /// </para>
    /// </summary>
    public ObservableCollection<CaseJour> Cases { get; } = [];

    [ObservableProperty]
    private string _moisCalendrier = "";

    /// <summary>Faux quand le projet n'a aucune occurrence datée : la grille resterait vide.</summary>
    [ObservableProperty]
    private bool _calendrierGarni;

    private DateOnly _moisAffiche = new(DateTime.Now.Year, DateTime.Now.Month, 1);

    [RelayCommand]
    private void MoisPrecedent() { _moisAffiche = _moisAffiche.AddMonths(-1); ChargerCalendrier(); }

    [RelayCommand]
    private void MoisSuivant() { _moisAffiche = _moisAffiche.AddMonths(1); ChargerCalendrier(); }

    /// <summary>
    /// Construit la grille du mois pour le projet ouvert. Six semaines pleines, comme la grille
    /// principale : une hauteur qui saute d'un mois à l'autre donne une impression de bougé.
    /// </summary>
    private void ChargerCalendrier()
    {
        Cases.Clear();
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        MoisCalendrier = fr.TextInfo.ToTitleCase(_moisAffiche.ToString("MMMM yyyy", fr));

        if (ProjetOuvert is not { } ouvert)
        {
            CalendrierGarni = false;
            return;
        }

        var categorie = _composition.Acces.Lire(
            () => _composition.Lecture.Projets().FirstOrDefault(p => p.Id == ouvert.Id)?.CategorieId);

        // Le lundi qui précède le 1er, puis 42 cases : même construction que §5.4.
        var premier = _moisAffiche;
        var decalage = ((int)premier.DayOfWeek + 6) % 7;
        var depart = premier.AddDays(-decalage);
        var debut = new DateTimeOffset(depart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fin = new DateTimeOffset(depart.AddDays(41).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var filtre = categorie is { } id ? new HashSet<Guid> { id } : null;
        var parJour = _composition.Acces
            .Lire(() => _composition.Calendrier.Occurrences(debut, fin, filtre))
            .GroupBy(o => DateOnly.FromDateTime(o.InstantUtc.ToLocalTime().DateTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var aujourdhui = DateOnly.FromDateTime(DateTime.Now);
        for (var i = 0; i < 42; i++)
        {
            var jour = depart.AddDays(i);
            parJour.TryGetValue(jour, out var duJour);
            var pastilles = (duJour ?? [])
                .Take(3)
                .Select(o => new PastilleAgenda(
                    o.Titre,
                    o.MontantCentimes is { } c && o.Sens is { } sens ? Format.EurosSigne(c, sens) : null,
                    o.Type))
                .ToList();

            Cases.Add(new CaseJour(
                Numero: jour.Day.ToString(),
                HorsMois: jour.Month != premier.Month,
                EstAujourdhui: jour == aujourdhui,
                Pastilles: pastilles,
                Debordement: duJour is { Count: > 3 } ? $"+{duJour.Count - 3}" : null));
        }

        CalendrierGarni = parJour.Count > 0;
    }

    [ObservableProperty]
    private string _nouveauNom = "";

    [ObservableProperty]
    private string _nouvelleTache = "";

    [ObservableProperty]
    private string? _message;

    /// <summary>
    /// Ce que le rattrapage des calendriers orphelins a rangé au démarrage. Séparé de
    /// <see cref="Message"/>, qui annonce des refus : ranger n'est pas une erreur, et le dire au
    /// même endroit — dans le panneau d'un projet, qui est fermé au lancement — ne le dirait à
    /// personne.
    /// </summary>
    [ObservableProperty]
    private string? _messageRattrapage;

    /// <summary>Nom en cours de saisie quand on renomme le projet ouvert. Valeur de travail : abandonner ne laisse rien.</summary>
    [ObservableProperty]
    private string _nomEnCours = "";

    /// <summary>Couleur en cours de choix, même raison.</summary>
    [ObservableProperty]
    private string _couleurEnCours = "";

    /// <summary>Vrai quand le formulaire de renommage du projet ouvert est déplié.</summary>
    [ObservableProperty]
    private bool _enEdition;

    public bool AucunProjet => Projets.Count == 0;

    /// <summary>Le projet ouvert, dont on voit et complète les tâches.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjetOuvert))]
    private Guid? _idOuvert;

    public LigneProjet? ProjetOuvert => Projets.FirstOrDefault(p => p.Id == IdOuvert);

    /// <summary>
    /// Couleurs de la palette catégorielle (§5.4) — terreuses et fonctionnelles. Attribuées à tour
    /// de rôle pour que deux projets voisins ne se confondent pas dans le calendrier.
    /// </summary>
    private static readonly string[] Palette =
        ["#4B7F8A", "#4A8C63", "#BB5A44", "#A97E3C", "#7A6AA6"];

    /// <summary>La même palette, atteignable depuis une liaison de vue (x:Bind exige une instance).</summary>
    public IReadOnlyList<string> Couleurs => Palette;

    public void Charger()
    {
        var (projets, taches) = _composition.Acces.Lire(() =>
        {
            var p = _composition.Lecture.Projets();
            return (p, p.ToDictionary(x => x.Id, x => _composition.Lecture.TachesDeProjet(x.Id)));
        });

        Projets.Clear();
        foreach (var projet in projets)
        {
            var lignes = taches[projet.Id].Select(t => new LigneTache
            {
                Id = t.Id,
                Titre = t.Titre,
                Priorite = t.Priorite ?? Priorite.Normale,
                Faite = t.Statut == StatutElement.Fait,
                Reportee = t.Statut == StatutElement.Reporte,
            }).ToList();

            Projets.Add(new LigneProjet
            {
                Id = projet.Id,
                Nom = projet.Nom,
                Couleur = string.IsNullOrWhiteSpace(projet.Couleur) ? Palette[0] : projet.Couleur,
                Statut = projet.Statut,
                Taches = lignes,
                Avancement = Avancement(lignes),
                Selectionne = projet.Id == IdOuvert,
            });
        }

        // Le projet ouvert a pu être fermé ou supprimé ailleurs : ne pas laisser un panneau vide.
        if (IdOuvert is { } id && Projets.All(p => p.Id != id))
            IdOuvert = null;

        OnPropertyChanged(nameof(AucunProjet));
        OnPropertyChanged(nameof(ProjetOuvert));
        ChargerCalendrier();
    }

    /// <summary>
    /// Range à la corbeille les calendriers de projets devenus <b>orphelins</b> : ceux dont le
    /// projet a été supprimé du temps où <c>SupprimerProjet</c> n'emportait que le projet. Ils
    /// restaient dans les filtres du calendrier sans rien à filtrer, et l'utilisateur n'avait aucun
    /// moyen de s'en défaire — un calendrier de projet ne se gère pas à la main (§5.4).
    /// <para>
    /// <b>Corriger l'état, pas chaque lecteur.</b> Masquer les orphelins à l'affichage aurait
    /// semblé plus prudent, mais trois écrans listent les mêmes catégories — la barre latérale, la
    /// grille du calendrier, la gestion des calendriers — et l'app Apple en listera autant. La
    /// règle « ne montre pas les orphelins » devrait être réécrite dans chacun ; le premier qui
    /// l'oublierait ramènerait le défaut. C'est la divergence silencieuse que la spec existe pour
    /// empêcher. Un seul état, lu partout de la même façon.
    /// </para>
    /// <para>
    /// <b>Rien n'est détruit</b> (filet 2, règle 11) : on passe par <c>Saisie.Supprimer</c>, qui
    /// <i>marque</i>. Les calendriers rangés sont dans la corbeille, restaurables, charge utile
    /// intacte — et le rangement se dit, via <see cref="MessageRattrapage"/> : une écriture que
    /// l'utilisateur n'a pas demandée ne doit pas être muette.
    /// </para>
    /// <para>
    /// <b>Une fois, au montage du module — surtout pas dans <see cref="Charger"/>.</b> Nettoyer au
    /// fil des lectures est le vrai piège : <c>Charger</c> est rappelé après chaque saisie et à
    /// chaque passage sur l'onglet, si bien qu'un calendrier restauré depuis la corbeille
    /// disparaîtrait sous les yeux de l'utilisateur dans la seconde, en boucle. Le rattrapage est
    /// donc une étape <i>nommée</i> du démarrage, jouée une fois par lancement, et <c>Charger</c>
    /// reste une lecture pure. Il est de surcroît idempotent : une fois rangé, un orphelin n'est
    /// plus rendu par <c>Lecture.Categories()</c>, la passe suivante n'écrit rien.
    /// </para>
    /// </summary>
    private void RattraperCalendriersOrphelins()
    {
        var ranges = _composition.Acces.Lire(() =>
        {
            // Un projet à la corbeille ne compte pas comme vivant : son calendrier doit suivre.
            // Un projet FERMÉ, si — la fermeture éteint le filtre, elle ne le supprime pas (§5.3).
            var vivants = _composition.Lecture.Projets()
                .Where(p => p.CategorieId is not null)
                .Select(p => p.CategorieId!.Value)
                .ToHashSet();

            var orphelins = _composition.Lecture.Categories()
                .Where(c => c.Origine == OrigineCategorie.Projet && !vivants.Contains(c.Id))
                .ToList();

            foreach (var orphelin in orphelins)
                _composition.Saisie.Supprimer(EntiteSynchro.Categorie, orphelin.Id);

            return orphelins.Count;
        });

        MessageRattrapage = ranges switch
        {
            0 => null,
            1 => "Un calendrier de projet supprimé traînait dans les filtres : il est à la corbeille.",
            _ => $"{ranges} calendriers de projets supprimés traînaient dans les filtres : "
                 + "ils sont à la corbeille.",
        };
    }

    private static string Avancement(IReadOnlyList<LigneTache> taches)
    {
        if (taches.Count == 0) return "Aucune tâche";
        var faites = taches.Count(t => t.Faite);
        var mot = taches.Count == 1 ? "tâche" : "tâches";
        return faites == 0 ? $"{taches.Count} {mot}" : $"{taches.Count} {mot} · {faites} faite(s)";
    }

    /// <summary>
    /// Crée un projet ET son calendrier (§5.3, §5.4). Les deux dans la même foulée : un projet
    /// dont le filtre serait à créer à la main ne serait pas « automatique ».
    /// </summary>
    [RelayCommand]
    private void Creer()
    {
        var nom = NouveauNom.Trim();
        if (nom.Length == 0) return;

        var couleur = Palette[Projets.Count % Palette.Length];
        var projet = new Projet { Nom = nom, Couleur = couleur, Statut = StatutProjet.Actif };

        // Origine = Projet : c'est ce qui distingue ce calendrier d'une catégorie créée à la main,
        // et ce qui permettra de le présenter à part sans tenir deux listes (§5.4).
        // L'identifiant est posé ICI, explicitement. Le laisser à Enregistrer (qui le génère
        // quand il est vide) rattacherait le projet à Guid.Empty : on lirait l'Id avant qu'il
        // existe, et le lien serait rompu en silence.
        var calendrier = new Categorie
        {
            Id = Guid.NewGuid(),
            Nom = nom,
            Couleur = couleur,
            Origine = OrigineCategorie.Projet,
        };
        projet.CategorieId = calendrier.Id;

        var refus = _composition.Acces.Lire(() =>
        {
            var r1 = _composition.Saisie.Enregistrer(calendrier, EntiteSynchro.Categorie);
            if (!r1.Reussi) return r1;
            return _composition.Saisie.Enregistrer(projet, EntiteSynchro.Projet);
        });

        if (!refus.Reussi)
        {
            Message = string.Join(" / ", refus.Erreurs.Select(e => e.Message));
            return;
        }

        NouveauNom = "";
        Message = null;
        IdOuvert = projet.Id;
        Charger();
        ApresChangement?.Invoke();
    }

    [RelayCommand]
    private void Ouvrir(LigneProjet projet)
    {
        IdOuvert = IdOuvert == projet.Id ? null : projet.Id;
        // Changer de projet referme le formulaire : un nom en cours de frappe appartient au projet
        // sur lequel on l'a tapé, pas au suivant.
        EnEdition = false;
        foreach (var ligne in Projets) ligne.Selectionne = ligne.Id == IdOuvert;
        OnPropertyChanged(nameof(ProjetOuvert));
        ChargerCalendrier();
    }

    /// <summary>
    /// Déplie le formulaire de renommage du projet ouvert. Il n'existait aucun moyen de changer le
    /// nom ou la couleur d'un projet : la convention d'architecture d'information du §5 veut que
    /// <b>tout ce qui est affiché se modifie</b>.
    /// </summary>
    [RelayCommand]
    private void Modifier()
    {
        if (ProjetOuvert is not { } ouvert) return;
        NomEnCours = ouvert.Nom;
        CouleurEnCours = ouvert.Couleur;
        EnEdition = true;
        Message = null;
    }

    /// <summary>Referme sans écrire. Les valeurs de travail sont jetées, l'original n'a pas bougé.</summary>
    [RelayCommand]
    private void AnnulerModification() => EnEdition = false;

    [RelayCommand]
    private void ChoisirCouleur(string couleur) => CouleurEnCours = couleur;

    /// <summary>
    /// Renomme et recolorie le projet ouvert — <b>et son calendrier</b> (§5.3, D-030). Le nom et la
    /// couleur d'un projet identifient ses occurrences partout où elles apparaissent : un calendrier
    /// resté sur l'ancien nom ferait mentir la liste des filtres.
    /// <para>
    /// Le formulaire ne se referme qu'en cas de succès : un refus qui effacerait la frappe de
    /// l'utilisateur serait une deuxième punition pour la même erreur.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void EnregistrerModification()
    {
        if (ProjetOuvert is not { } ouvert) return;

        var nom = NomEnCours.Trim();
        if (nom.Length == 0)
        {
            Message = "Le nom ne peut pas être vide.";
            return;
        }

        // Une couleur jamais choisie n'est pas une couleur vide : on garde celle du projet.
        var couleur = string.IsNullOrWhiteSpace(CouleurEnCours) ? ouvert.Couleur : CouleurEnCours;

        var ecrit = ModifierProjet(
            ouvert.Id,
            projet => { projet.Nom = nom; projet.Couleur = couleur; },
            calendrier => { calendrier.Nom = nom; calendrier.Couleur = couleur; });

        if (ecrit) EnEdition = false;
    }

    /// <summary>
    /// Ajoute une tâche au projet ouvert. <c>ProjetId</c> est posé ici et jamais laissé vide : en
    /// V1 une tâche sans projet est refusée par le cœur (D-027).
    /// </summary>
    [RelayCommand]
    private void AjouterTache()
    {
        if (ProjetOuvert is not { } projet) return;
        var titre = NouvelleTache.Trim();
        if (titre.Length == 0) return;

        var tache = new Element
        {
            Type = TypeElement.Tache,
            Titre = titre,
            Statut = StatutElement.AFaire,
            ProjetId = projet.Id,
            Priorite = Priorite.Normale,
        };

        var resultat = _composition.Acces.Lire(
            () => _composition.Saisie.Enregistrer(tache, EntiteSynchro.Element));

        if (!resultat.Reussi)
        {
            Message = string.Join(" / ", resultat.Erreurs.Select(e => e.Message));
            return;
        }

        NouvelleTache = "";
        Message = null;
        Charger();
        ApresChangement?.Invoke();
    }

    /// <summary>
    /// Coche ou décoche une tâche (§3.1 : `a_faire` ↔ `fait`). La ligne est modifiée <b>sur
    /// place</b> : pas de rechargement, donc aucune ligne ne bouge sous le doigt.
    /// </summary>
    [RelayCommand]
    private void BasculerTache(LigneTache tache)
    {
        var vise = !tache.Faite;
        if (!Persister(tache.Id, e => e.Statut = vise ? StatutElement.Fait : StatutElement.AFaire))
        {
            tache.RemettreCocheAuReel();
            return;
        }

        tache.Faite = vise;
        RecalculerAvancement();
    }

    /// <summary>Fait tourner la priorité — trois valeurs, un seul geste, pas de menu à ouvrir.</summary>
    [RelayCommand]
    private void CyclerPriorite(LigneTache tache)
    {
        var vise = tache.Priorite switch
        {
            Priorite.Basse => Priorite.Normale,
            Priorite.Normale => Priorite.Haute,
            _ => Priorite.Basse,
        };

        if (!Persister(tache.Id, e => e.Priorite = vise)) return;

        // La priorité ne trie plus la liste (voir ServiceLecture.TachesDeProjet) : changer une
        // étiquette ne doit pas déplacer la ligne qu'on vient de viser.
        tache.Priorite = vise;
    }

    /// <summary>Met à jour l'avancement du projet ouvert sans reconstruire ses lignes.</summary>
    private void RecalculerAvancement()
    {
        if (ProjetOuvert is { } ouvert) ouvert.Avancement = Avancement(ouvert.Taches);
    }

    [RelayCommand]
    private void SupprimerTache(LigneTache tache)
    {
        // Filet 2 : marquage, jamais destruction — la tâche part à la corbeille.
        _composition.Acces.Lire(() => _composition.Saisie.Supprimer(EntiteSynchro.Element, tache.Id));
        Charger();
        ApresChangement?.Invoke();
    }

    /// <summary>
    /// Change le statut du projet. Passer à `termine` ou `en_pause` reporte ses tâches `a_faire`
    /// (§3.2) — mais <b>c'est le serveur qui le fait</b>, à l'application du push : le rejouer ici
    /// dupliquerait une règle métier dans les deux apps (règle 2). La liste se remettra à jour au
    /// prochain pull.
    /// <para>
    /// <b>Le calendrier n'est pas touché, et c'est la règle</b> (§5.3, D-030) : fermer n'est pas
    /// supprimer. Le projet et son histoire existent toujours, donc son filtre reste — la barre
    /// latérale l'éteint simplement par défaut tant qu'il n'est plus actif.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void ChangerStatut(LigneProjet ligne)
    {
        var suivant = ligne.Statut switch
        {
            StatutProjet.Actif => StatutProjet.EnPause,
            StatutProjet.EnPause => StatutProjet.Termine,
            _ => StatutProjet.Actif,
        };

        ModifierProjet(ligne.Id, p => p.Statut = suivant);
    }

    /// <summary>
    /// Met le projet <b>et son calendrier</b> à la corbeille (§5.3, D-030). Symétrie exacte de
    /// <see cref="Creer"/>, qui crée les deux : ce qui naît ensemble part ensemble. L'asymétrie
    /// était un vrai défaut signalé à l'usage — le calendrier survivait dans les filtres, sans rien
    /// à filtrer et sans moyen de s'en défaire.
    /// <para>
    /// Filet 2 : les deux sont <b>marqués</b>, jamais détruits, et la corbeille les rend tous deux.
    /// Le calendrier ne part qu'une fois le projet parti : deux moitiés de suppression vaudraient
    /// moins que rien du tout.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void SupprimerProjet(LigneProjet ligne)
    {
        var resultat = _composition.Acces.Lire(() =>
        {
            var calendrier = _composition.Lecture.Projets()
                .FirstOrDefault(p => p.Id == ligne.Id)?.CategorieId;

            var projetRange = _composition.Saisie.Supprimer(EntiteSynchro.Projet, ligne.Id);
            if (!projetRange.Reussi || calendrier is not { } id) return projetRange;

            return _composition.Saisie.Supprimer(EntiteSynchro.Categorie, id);
        });

        // Un refus avalé ici laisserait un bouton qui ne fait rien, sans le dire.
        Message = resultat.Reussi ? null : string.Join(" / ", resultat.Erreurs.Select(e => e.Message));

        if (IdOuvert == ligne.Id)
        {
            IdOuvert = null;
            EnEdition = false;
        }

        Charger();
        ApresChangement?.Invoke();
    }

    /// <summary>
    /// Écrit un changement de tâche dans la base et rend vrai s'il est passé. <b>Ne recharge
    /// pas</b> : l'appelant met la ligne à jour sur place, ce qui garde la liste immobile.
    /// </summary>
    private bool Persister(Guid id, Action<Element> changement)
    {
        var resultat = _composition.Acces.Lire(() =>
        {
            var element = _composition.Lecture.Actifs(TypeElement.Tache).FirstOrDefault(e => e.Id == id);
            if (element is null) return null;
            changement(element);
            return _composition.Saisie.Enregistrer(element, EntiteSynchro.Element);
        });

        if (resultat is null)
        {
            Message = "Tâche introuvable — elle a peut-être été supprimée ailleurs.";
            Charger();
            return false;
        }

        if (!resultat.Reussi)
        {
            Message = string.Join(" / ", resultat.Erreurs.Select(e => e.Message));
            return false;
        }

        Message = null;
        // L'état de synchro change (outbox) : la coquille doit le voir, mais la liste reste en place.
        ApresChangementLeger?.Invoke();
        return true;
    }

    /// <summary>
    /// Écrit un changement de projet, et — quand <paramref name="surCalendrier"/> est fourni — le
    /// changement jumeau sur son calendrier, dans la même passe (§5.3, D-030). Rend vrai si tout
    /// est passé.
    /// </summary>
    private bool ModifierProjet(Guid id, Action<Projet> changement, Action<Categorie>? surCalendrier = null)
    {
        // Le résultat est REGARDÉ : un refus avalé ici donnerait un bouton qui ne fait rien, sans
        // le dire — et c'est justement ce genre de silence qui coûte des heures à diagnostiquer.
        var resultat = _composition.Acces.Lire(() =>
        {
            var projet = _composition.Lecture.Projets().FirstOrDefault(p => p.Id == id);
            if (projet is null) return null;
            changement(projet);

            var ecrit = _composition.Saisie.Enregistrer(projet, EntiteSynchro.Projet);
            if (!ecrit.Reussi || surCalendrier is null || projet.CategorieId is not { } idCalendrier)
                return ecrit;

            // Le calendrier ne suit QUE si le projet est passé : les faire diverger serait pire que
            // ne rien changer du tout. Un calendrier absent (déjà à la corbeille) n'est pas une
            // erreur — le projet, lui, a bien été renommé.
            var calendrier = _composition.Lecture.Categories().FirstOrDefault(c => c.Id == idCalendrier);
            if (calendrier is null) return ecrit;

            surCalendrier(calendrier);
            return _composition.Saisie.Enregistrer(calendrier, EntiteSynchro.Categorie);
        });

        Message = resultat switch
        {
            null => "Projet introuvable — il a peut-être été supprimé ailleurs.",
            { Reussi: false } => string.Join(" / ", resultat.Erreurs.Select(e => e.Message)),
            _ => null,
        };

        Charger();
        ApresChangement?.Invoke();
        return resultat is { Reussi: true };
    }
}
