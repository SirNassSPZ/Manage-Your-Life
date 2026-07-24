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
        => depot.Enumerer(EntiteSynchro.Categorie)
            .Select(e => SerialisationCanonique.Deserialiser<Categorie>(e.PayloadCanonique))
            .Where(c => !c.Supprime)
            .OrderBy(c => c.Nom, StringComparer.CurrentCulture)
            .ToList();
}
