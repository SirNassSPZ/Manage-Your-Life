using System.Text.Json;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>Horloge fixe pour des tests déterministes (dates d'audit, arbitrage).</summary>
public sealed class HorlogeFixe(DateTimeOffset maintenant) : IHorloge
{
    public DateTimeOffset MaintenantUtc { get; set; } = maintenant;
}

/// <summary>Fabrique d'objets pour les tests de l'app Windows (base locale, outbox, synchro client).</summary>
public static class FabriqueLocale
{
    public static readonly Guid AppareilA = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly DateTimeOffset T0 = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Base locale SQLite en mémoire : la connexion unique de <see cref="BaseLocale"/> la maintient vivante.</summary>
    public static BaseLocale BaseMemoire() => new("Data Source=:memory:");

    /// <summary>Un Élément « frais » tel que saisi dans l'UI : sans id ni audit (le service les pose).</summary>
    public static Element NouvelleFacture(string titre = "Loyer", long montant = 80000, string? recurrence = null)
        => new()
        {
            Type = TypeElement.Facture,
            Titre = titre,
            DateDebut = new DateTimeOffset(2026, 7, 5, 7, 0, 0, TimeSpan.Zero),
            Fuseau = "Europe/Paris",
            Recurrence = recurrence,
            MontantCentimes = montant,
            Devise = "EUR",
            Sens = Sens.Sortie,
            Statut = StatutElement.AVenir,
        };

    public static Element Facture(
        Guid? id = null, string titre = "Loyer", long montant = 80000,
        DateTimeOffset? dateDebut = null, StatutElement statut = StatutElement.AVenir,
        string? recurrence = null, bool supprime = false, int version = 1,
        DateTimeOffset? dateModification = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Type = TypeElement.Facture,
            Titre = titre,
            DateDebut = dateDebut ?? new DateTimeOffset(2026, 7, 5, 7, 0, 0, TimeSpan.Zero),
            Fuseau = "Europe/Paris",
            Recurrence = recurrence,
            MontantCentimes = montant,
            Devise = "EUR",
            Sens = Sens.Sortie,
            Statut = statut,
            DateCreation = T0,
            DateModification = dateModification ?? T0,
            AppareilSource = AppareilA,
            Version = version,
            Supprime = supprime,
            DateSuppression = supprime ? (dateModification ?? T0) : null,
        };

    public static ReglageSolde Reglage(long centimes = 150000, DateOnly? date = null, int version = 1)
        => new()
        {
            Id = ReglageSolde.IdSoldeReference,
            SoldeReferenceCentimes = centimes,
            SoldeReferenceDate = date ?? new DateOnly(2026, 7, 1),
            DateCreation = T0,
            DateModification = T0,
            AppareilSource = AppareilA,
            Version = version,
        };

    /// <summary>Construit l'état d'une entité (payload canonique) tel qu'il serait stocké/tiré.</summary>
    public static EtatEntite Etat<T>(T entite, EntiteSynchro type) where T : EntiteSynchronisee
        => new(type, entite.Id, entite.Version, entite.DateModification, entite.Supprime,
            entite.ServerSeq ?? 0, SerialisationCanonique.Serialiser(entite));

    /// <summary>Construit une entrée d'outbox (§6.2) pour une entité.</summary>
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
}
