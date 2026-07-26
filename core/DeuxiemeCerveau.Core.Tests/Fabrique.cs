using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Core.Tests;

/// <summary>Fabrique d'objets de test valides — chaque test ne précise que ce qui le concerne.</summary>
public static class Fabrique
{
    public static readonly Guid AppareilA = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid AppareilB = new("bbbbbbbb-0000-0000-0000-000000000002");

    public static readonly DateTimeOffset T0 = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    public static Element Facture(
        Guid? id = null,
        string titre = "Loyer",
        long montant = 80000,
        DateTimeOffset? dateDebut = null,
        string? fuseau = "Europe/Paris",
        string? recurrence = null,
        StatutElement statut = StatutElement.AVenir,
        Guid? appareil = null,
        int version = 1,
        DateTimeOffset? dateModification = null)
    {
        var quand = dateModification ?? T0;
        return new Element
        {
            Id = id ?? Guid.NewGuid(),
            Type = TypeElement.Facture,
            Titre = titre,
            DateDebut = dateDebut ?? new DateTimeOffset(2026, 7, 5, 7, 0, 0, TimeSpan.Zero),
            Fuseau = fuseau,
            Recurrence = recurrence,
            MontantCentimes = montant,
            Devise = "EUR",
            Sens = Modele.Sens.Sortie,
            Statut = statut,
            DateCreation = quand,
            DateModification = quand,
            AppareilSource = appareil ?? AppareilA,
            Version = version,
        };
    }

    public static Element Revenu(
        string titre = "Salaire",
        long montant = 220000,
        DateTimeOffset? dateDebut = null,
        string? fuseau = "Europe/Paris",
        string? recurrence = null,
        StatutElement statut = StatutElement.Attendu)
    {
        var element = Facture(titre: titre, montant: montant, dateDebut: dateDebut,
            fuseau: fuseau, recurrence: recurrence, statut: StatutElement.AVenir);
        element.Type = TypeElement.Revenu;
        element.Sens = Modele.Sens.Entree;
        element.Statut = statut;
        return element;
    }

    /// <summary>
    /// Projet de rattachement par défaut des tâches. Depuis D-027, une tâche V1 appartient
    /// toujours à un projet (§5.3) : une fabrique qui en produirait sans projet ne fabriquerait
    /// que des Éléments invalides.
    /// </summary>
    public static readonly Guid ProjetParDefaut = new("6f1d9c3a-0000-4000-8000-00000000d027");

    public static Element Tache(
        Guid? id = null,
        string titre = "Réviser",
        StatutElement statut = StatutElement.AFaire,
        Guid? projetId = null,
        Guid? appareil = null,
        int version = 1,
        DateTimeOffset? dateModification = null)
    {
        var quand = dateModification ?? T0;
        return new Element
        {
            Id = id ?? Guid.NewGuid(),
            Type = TypeElement.Tache,
            Titre = titre,
            Statut = statut,
            ProjetId = projetId ?? ProjetParDefaut,
            DateCreation = quand,
            DateModification = quand,
            AppareilSource = appareil ?? AppareilA,
            Version = version,
        };
    }

    public static Element Note(string titre = "Brouillon", StatutElement statut = StatutElement.Active)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = TypeElement.Note,
            Titre = titre,
            Description = "texte libre",
            Statut = statut,
            DateCreation = T0,
            DateModification = T0,
            AppareilSource = AppareilA,
            Version = 1,
        };

    /// <summary>
    /// Une envie d'achat — sans prix par défaut : depuis D-027 il est <b>facultatif</b>, et une
    /// envie se note souvent avant qu'on en connaisse le prix.
    /// </summary>
    public static Element Envie(string titre = "Casque audio", StatutElement statut = StatutElement.Idee)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = TypeElement.Envie,
            Titre = titre,
            Statut = statut,
            DateCreation = T0,
            DateModification = T0,
            AppareilSource = AppareilA,
            Version = 1,
        };

    public static Projet Projet(
        Guid? id = null,
        string nom = "MMA",
        StatutProjet statut = StatutProjet.Actif,
        Guid? appareil = null,
        int version = 1,
        DateTimeOffset? dateModification = null)
    {
        var quand = dateModification ?? T0;
        return new Projet
        {
            Id = id ?? Guid.NewGuid(),
            Nom = nom,
            Couleur = "#3366FF",
            Statut = statut,
            DateCreation = quand,
            DateModification = quand,
            AppareilSource = appareil ?? AppareilA,
            Version = version,
        };
    }

    /// <summary>
    /// Une catégorie valide. <c>ordre</c> et <c>icone</c> sont facultatifs (§3.3, v3.3) : par
    /// défaut absents, ce qui est le cas de toutes les catégories d'avant la migration 004.
    /// </summary>
    public static Categorie Categorie(string nom = "santé", int? ordre = null, string? icone = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Nom = nom,
            Couleur = "#00AA55",
            Origine = OrigineCategorie.Transversale,
            Ordre = ordre,
            Icone = icone,
            DateCreation = T0,
            DateModification = T0,
            AppareilSource = AppareilA,
            Version = 1,
        };

    /// <summary>Construit l'entrée d'outbox d'une entité — enveloppe cohérente avec le payload (§6.2).</summary>
    public static ChangementPush Changement<T>(T entite, EntiteSynchro type, Guid? changeId = null)
        where T : EntiteSynchronisee
        => new()
        {
            ChangeId = changeId ?? Guid.NewGuid(),
            Entite = type,
            EntiteId = entite.Id,
            Version = entite.Version,
            DateModification = entite.DateModification,
            AppareilId = entite.AppareilSource,
            Payload = JsonSerializer.SerializeToElement(entite, SerialisationCanonique.Options),
        };

    public static LotPush Lot(Guid appareil, params ChangementPush[] changements)
        => new() { AppareilId = appareil, Changements = [.. changements] };

    /// <summary>Copie profonde par le canal canonique — simule un autre appareil éditant l'entité.</summary>
    public static Element Copier(Element element)
        => SerialisationCanonique.Deserialiser<Element>(SerialisationCanonique.Serialiser(element));
}
