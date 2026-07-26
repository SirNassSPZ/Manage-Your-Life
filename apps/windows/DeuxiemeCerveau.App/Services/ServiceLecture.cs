using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Services;

/// <summary>
/// Vues de lecture de l'app (§5) — construites depuis la base locale, sans réseau (local-first). Ne
/// stocke aucune donnée dérivée (règle 9) : filtre et trie l'état courant à la lecture.
/// </summary>
public sealed class ServiceLecture(DepotLocal depot)
{
    private IEnumerable<Element> Elements()
        => depot.Enumerer(EntiteSynchro.Element)
            .Select(e => SerialisationCanonique.Deserialiser<Element>(e.PayloadCanonique));

    /// <summary>Éléments actifs (hors corbeille), filtrables par type, catégorie et projet (§5.4).</summary>
    public IReadOnlyList<Element> Actifs(TypeElement? type = null, Guid? categorie = null, Guid? projet = null)
        => Elements()
            .Where(e => !e.Supprime)
            .Where(e => type is null || e.Type == type)
            .Where(e => categorie is null || e.Categories.Contains(categorie.Value))
            .Where(e => projet is null || e.ProjetId == projet)
            .OrderBy(e => e.DateDebut ?? DateTimeOffset.MaxValue)
            .ThenBy(e => e.Titre, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>Contenu de la corbeille (§5.6) : les éléments marqués supprimés, plus récents d'abord.</summary>
    public IReadOnlyList<Element> Corbeille()
        => Elements()
            .Where(e => e.Supprime)
            .OrderByDescending(e => e.DateSuppression ?? e.DateModification)
            .ToList();

    /// <summary>Notes libres (§5.5) : Éléments « note », hors corbeille, plus récentes d'abord.</summary>
    public IReadOnlyList<Element> Notes()
        => Elements()
            .Where(e => !e.Supprime && e.Type == TypeElement.Note)
            .OrderByDescending(e => e.DateModification)
            .ToList();

    /// <summary>Catégories actives — les filtres affichables/masquables du calendrier (§5.4).</summary>
    public IReadOnlyList<Categorie> Categories()
        => ToutesCategories().Where(c => !c.Supprime)
            .OrderBy(c => c.Nom, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>
    /// Catégories à la corbeille (§5.6). Le filet 2 vaut pour <b>toute</b> entité synchronisée
    /// (D-006), pas seulement pour l'Élément : une catégorie supprimée doit rester restaurable,
    /// sans quoi son marquage serait une destruction déguisée.
    /// </summary>
    public IReadOnlyList<Categorie> CorbeilleCategories()
        => ToutesCategories().Where(c => c.Supprime)
            .OrderByDescending(c => c.DateSuppression ?? c.DateModification)
            .ToList();

    private IEnumerable<Categorie> ToutesCategories()
        => depot.Enumerer(EntiteSynchro.Categorie)
            .Select(e => SerialisationCanonique.Deserialiser<Categorie>(e.PayloadCanonique));

    /// <summary>
    /// Projets hors corbeille (§3.2, V1 depuis D-027). Les actifs d'abord : un projet en pause ou
    /// terminé reste consultable, mais il n'a plus à occuper le haut de la liste.
    /// </summary>
    public IReadOnlyList<Projet> Projets()
        => TousProjets().Where(p => !p.Supprime)
            .OrderBy(p => p.Statut == StatutProjet.Actif ? 0 : 1)
            .ThenBy(p => p.Nom, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>Projets à la corbeille (§5.6) — le filet 2 vaut pour toute entité (D-006).</summary>
    public IReadOnlyList<Projet> CorbeilleProjets()
        => TousProjets().Where(p => p.Supprime)
            .OrderByDescending(p => p.DateSuppression ?? p.DateModification)
            .ToList();

    /// <summary>
    /// Les tâches d'un projet (§5.3). Tri : l'ordre manuel d'abord quand il est posé — c'est
    /// justement ce que l'utilisateur a demandé en le posant — puis la priorité, puis le titre.
    /// </summary>
    public IReadOnlyList<Element> TachesDeProjet(Guid projet)
        => Elements()
            .Where(e => !e.Supprime && e.Type == TypeElement.Tache && e.ProjetId == projet)
            .OrderBy(e => e.OrdreManuel ?? int.MaxValue)
            .ThenByDescending(e => e.Priorite ?? Priorite.Normale)
            .ThenBy(e => e.Titre, StringComparer.CurrentCulture)
            .ToList();

    private IEnumerable<Projet> TousProjets()
        => depot.Enumerer(EntiteSynchro.Projet)
            .Select(e => SerialisationCanonique.Deserialiser<Projet>(e.PayloadCanonique));
}
