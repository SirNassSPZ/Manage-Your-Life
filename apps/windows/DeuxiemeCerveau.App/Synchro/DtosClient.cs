using System.Text.Json;
using System.Text.Json.Serialization;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Synchro;

// Vue CLIENT du contrat §8 : ces DTOs décrivent le format du fil tel que l'app le lit. L'app Apple
// définira les siens depuis §8 de la même façon (langages différents). Le format est validé contre la
// sortie RÉELLE du serveur par les tests (aller-retour JSON via SerialisationCanonique) — toute
// divergence de nom de champ y échoue franchement.

/// <summary>Réponse de <c>POST /devices/register</c>.</summary>
public sealed record ReponseEnregistrementClient(
    [property: JsonPropertyName("appareil_id")] Guid AppareilId);

/// <summary>Résultat serveur d'un changement poussé (§6.2.4).</summary>
public sealed record ResultatChangementClient(
    [property: JsonPropertyName("change_id")] Guid ChangeId,
    ResultatChangement Resultat,
    bool Conflit,
    [property: JsonPropertyName("server_seq")] long ServerSeq,
    bool Rejoue);

public sealed record ChangementInduitClient(
    [property: JsonPropertyName("change_id")] Guid ChangeId,
    EntiteSynchro Entite,
    [property: JsonPropertyName("element_id")] Guid EntiteId,
    [property: JsonPropertyName("server_seq")] long ServerSeq);

/// <summary>Réponse de <c>POST /sync/push</c>.</summary>
public sealed record ReponsePushClient(
    IReadOnlyList<ResultatChangementClient> Resultats,
    [property: JsonPropertyName("changements_induits")] IReadOnlyList<ChangementInduitClient> ChangementsInduits);

/// <summary>Une entité renvoyée par le pull : enveloppe + payload canonique brut.</summary>
public sealed record EntitePullClient(
    EntiteSynchro Entite,
    Guid Id,
    int Version,
    [property: JsonPropertyName("date_modification")] DateTimeOffset DateModification,
    bool Supprime,
    [property: JsonPropertyName("server_seq")] long ServerSeq,
    JsonElement Payload);

public sealed record PurgePullClient(
    EntiteSynchro Entite,
    Guid Id,
    [property: JsonPropertyName("server_seq")] long ServerSeq,
    [property: JsonPropertyName("purge_le")] DateTimeOffset PurgeLe);

/// <summary>Réponse de <c>GET /sync/pull</c> : entités et purges depuis le curseur, + nouveau curseur.</summary>
public sealed record ReponsePullClient(
    IReadOnlyList<EntitePullClient> Entites,
    IReadOnlyList<PurgePullClient> Purges,
    long Curseur,
    bool Encore);

/// <summary>Résultat d'une demande de purge (§5.6, D-010).</summary>
public sealed record ResultatPurgeClient(
    [property: JsonPropertyName("change_id")] Guid ChangeId,
    StatutPurge Statut,
    [property: JsonPropertyName("server_seq")] long ServerSeq,
    bool Rejoue,
    string? Motif);

/// <summary>Réponse de <c>POST /purge</c>.</summary>
public sealed record ReponsePurgeClient(
    IReadOnlyList<ResultatPurgeClient> Resultats,
    [property: JsonPropertyName("purges_induites")] IReadOnlyList<ChangementInduitClient> PurgesInduites);
