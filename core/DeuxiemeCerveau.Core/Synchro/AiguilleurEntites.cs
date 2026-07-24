using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Validation;

namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// Aiguillage par type d'entité synchronisée (D-006) : désérialisation, validation et sérialisation
/// canoniques, définis <b>une seule fois</b> et partagés par le serveur (<see cref="ProcesseurPush"/>)
/// et par le client (saisie locale, application du pull). Un aiguillage dupliqué qui divergerait est
/// exactement le risque n° 1 du projet (garde-fou-synchro) — les deux côtés C# passent par ici.
/// </summary>
public static class AiguilleurEntites
{
    public static EntiteSynchronisee Deserialiser(EntiteSynchro type, JsonElement payload) => type switch
    {
        EntiteSynchro.Element => SerialisationCanonique.Deserialiser<Element>(payload),
        EntiteSynchro.Categorie => SerialisationCanonique.Deserialiser<Categorie>(payload),
        EntiteSynchro.Projet => SerialisationCanonique.Deserialiser<Projet>(payload),
        EntiteSynchro.Budget => SerialisationCanonique.Deserialiser<Budget>(payload),
        EntiteSynchro.PieceJointe => SerialisationCanonique.Deserialiser<PieceJointe>(payload),
        EntiteSynchro.Reglage => SerialisationCanonique.Deserialiser<ReglageSolde>(payload),
        _ => throw new JsonException($"Entité inconnue : {type}."),
    };

    public static EntiteSynchronisee Deserialiser(EntiteSynchro type, string payloadCanonique)
    {
        using var doc = JsonDocument.Parse(payloadCanonique);
        return Deserialiser(type, doc.RootElement);
    }

    public static IReadOnlyList<ErreurValidation> Valider(EntiteSynchro type, EntiteSynchronisee entite) => type switch
    {
        EntiteSynchro.Element => ValidateurElement.Valider((Element)entite),
        EntiteSynchro.Categorie => ValidateurEntites.Valider((Categorie)entite),
        EntiteSynchro.Projet => ValidateurEntites.Valider((Projet)entite),
        EntiteSynchro.Budget => ValidateurEntites.Valider((Budget)entite),
        EntiteSynchro.PieceJointe => ValidateurEntites.Valider((PieceJointe)entite),
        EntiteSynchro.Reglage => ValidateurEntites.Valider((ReglageSolde)entite),
        _ => [new ErreurValidation("entite", "entite_inconnue", $"Entité inconnue : {type}.")],
    };

    public static string Serialiser(EntiteSynchro type, EntiteSynchronisee entite) => type switch
    {
        EntiteSynchro.Element => SerialisationCanonique.Serialiser((Element)entite),
        EntiteSynchro.Categorie => SerialisationCanonique.Serialiser((Categorie)entite),
        EntiteSynchro.Projet => SerialisationCanonique.Serialiser((Projet)entite),
        EntiteSynchro.Budget => SerialisationCanonique.Serialiser((Budget)entite),
        EntiteSynchro.PieceJointe => SerialisationCanonique.Serialiser((PieceJointe)entite),
        EntiteSynchro.Reglage => SerialisationCanonique.Serialiser((ReglageSolde)entite),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
