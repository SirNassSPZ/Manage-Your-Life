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
/// </summary>
public sealed partial class VueModeleProjets : ObservableObject
{
    private readonly Composition _composition;

    public VueModeleProjets(Composition composition)
    {
        _composition = composition;
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

    [ObservableProperty]
    private string _nouveauNom = "";

    [ObservableProperty]
    private string _nouvelleTache = "";

    [ObservableProperty]
    private string? _message;

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
        foreach (var ligne in Projets) ligne.Selectionne = ligne.Id == IdOuvert;
        OnPropertyChanged(nameof(ProjetOuvert));
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

    [RelayCommand]
    private void SupprimerProjet(LigneProjet ligne)
    {
        _composition.Acces.Lire(() => _composition.Saisie.Supprimer(EntiteSynchro.Projet, ligne.Id));
        if (IdOuvert == ligne.Id) IdOuvert = null;
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

    private void ModifierProjet(Guid id, Action<Projet> changement)
    {
        // Le résultat est REGARDÉ : un refus avalé ici donnerait un bouton qui ne fait rien, sans
        // le dire — et c'est justement ce genre de silence qui coûte des heures à diagnostiquer.
        var resultat = _composition.Acces.Lire(() =>
        {
            var projet = _composition.Lecture.Projets().FirstOrDefault(p => p.Id == id);
            if (projet is null) return null;
            changement(projet);
            return _composition.Saisie.Enregistrer(projet, EntiteSynchro.Projet);
        });

        if (resultat is { Reussi: false })
            Message = string.Join(" / ", resultat.Erreurs.Select(e => e.Message));

        Charger();
        ApresChangement?.Invoke();
    }
}
