using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Api.Tests;

/// <summary>Fabrique d'objets pour les tests de contrat de l'API (§8).</summary>
public static class FabriqueApi
{
    public static readonly Guid AppareilA = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly DateTimeOffset T0 = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    public static Element Facture(
        Guid? id = null, string titre = "Loyer", long montant = 80000,
        DateTimeOffset? dateDebut = null, StatutElement statut = StatutElement.AVenir,
        string? recurrence = null, bool supprime = false)
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
            DateModification = T0,
            AppareilSource = AppareilA,
            Version = 1,
            Supprime = supprime,
            DateSuppression = supprime ? T0 : null,
        };

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

    public static LotPush Lot(params ChangementPush[] changements)
        => new() { AppareilId = AppareilA, Changements = [.. changements] };

    public static Element Copier(Element element)
        => SerialisationCanonique.Deserialiser<Element>(SerialisationCanonique.Serialiser(element));

    public static Projet Copier2(Projet projet)
        => SerialisationCanonique.Deserialiser<Projet>(SerialisationCanonique.Serialiser(projet));
}
