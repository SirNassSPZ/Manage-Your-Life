using System.Text.Json;
using System.Text.Json.Serialization;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Api.Contrats;

// DTOs du contrat d'API (§8). Ils cadrent le fil JSON ; toute la logique vit dans le cœur (règle 4).

/// <summary>Réponse de <c>POST /devices/register</c> (§8).</summary>
public sealed record DemandeEnregistrementAppareil(string Nom, string Plateforme);

public sealed record ReponseEnregistrementAppareil(
    [property: JsonPropertyName("appareil_id")] Guid AppareilId);

/// <summary>Un résultat de changement renvoyé au client (§6.2.4).</summary>
public sealed record ResultatChangementDto(
    [property: JsonPropertyName("change_id")] Guid ChangeId,
    ResultatChangement Resultat,
    bool Conflit,
    [property: JsonPropertyName("server_seq")] long ServerSeq,
    bool Rejoue);

public sealed record ChangementInduitDto(
    [property: JsonPropertyName("change_id")] Guid ChangeId,
    EntiteSynchro Entite,
    [property: JsonPropertyName("element_id")] Guid EntiteId,
    [property: JsonPropertyName("server_seq")] long ServerSeq);

/// <summary>Réponse de <c>POST /sync/push</c> : change_id confirmés + conflits archivés (§8).</summary>
public sealed record ReponsePushDto(
    IReadOnlyList<ResultatChangementDto> Resultats,
    [property: JsonPropertyName("changements_induits")] IReadOnlyList<ChangementInduitDto> ChangementsInduits);

/// <summary>Une entité renvoyée par le pull : enveloppe + payload canonique (objet JSON, non échappé).</summary>
public sealed record EntitePullDto(
    EntiteSynchro Entite,
    Guid Id,
    int Version,
    [property: JsonPropertyName("date_modification")] DateTimeOffset DateModification,
    bool Supprime,
    [property: JsonPropertyName("server_seq")] long ServerSeq,
    JsonElement Payload);

public sealed record PurgePullDto(
    EntiteSynchro Entite,
    Guid Id,
    [property: JsonPropertyName("server_seq")] long ServerSeq,
    [property: JsonPropertyName("purge_le")] DateTimeOffset PurgeLe);

/// <summary>Réponse de <c>GET /sync/pull</c> : entités et purges modifiées depuis le curseur (§8, D-010).</summary>
public sealed record ReponsePullDto(
    IReadOnlyList<EntitePullDto> Entites,
    IReadOnlyList<PurgePullDto> Purges,
    long Curseur,
    bool Encore);

/// <summary>Corps de <c>PUT /settings/solde-reference</c> (§3.4, §8).</summary>
public sealed record DemandeRecalageSolde(
    [property: JsonPropertyName("change_id")] Guid ChangeId,
    [property: JsonPropertyName("solde_reference_centimes")] long SoldeReferenceCentimes,
    [property: JsonPropertyName("solde_reference_date")] DateOnly SoldeReferenceDate,
    [property: JsonPropertyName("date_modification")] DateTimeOffset DateModification,
    [property: JsonPropertyName("appareil_id")] Guid AppareilId);

/// <summary>Un mois de la projection budgétaire (§5.1, §8).</summary>
public sealed record MoisProjeteDto(
    string Mois,
    [property: JsonPropertyName("ouverture_centimes")] long? OuvertureCentimes,
    [property: JsonPropertyName("entrees_centimes")] long EntreesCentimes,
    [property: JsonPropertyName("sorties_centimes")] long SortiesCentimes,
    [property: JsonPropertyName("cloture_centimes")] long? ClotureCentimes,
    bool Decouvert,
    [property: JsonPropertyName("avant_reference")] bool AvantReference);

/// <param name="SoldeCourantCentimes">
/// Le solde à l'instant de l'appel (§5.1) — le chiffre que l'app affiche en grand. Il découle des
/// Éléments encodés, contrairement au solde de référence qui ne bouge qu'au recalage (§3.4).
/// </param>
public sealed record ReponseProjectionDto(
    IReadOnlyList<MoisProjeteDto> Mois,
    [property: JsonPropertyName("solde_courant_centimes")] long SoldeCourantCentimes);

/// <summary>
/// Confrontation d'un montant au budget projeté (§5.1bis, §8). Les <b>deux</b> cascades sont
/// rendues : l'app doit pouvoir montrer l'écart mois par mois, pas seulement un oui/non.
/// </summary>
public sealed record ReponseConfrontationDto(
    bool Passe,
    [property: JsonPropertyName("montant_centimes")] long MontantCentimes,
    [property: JsonPropertyName("mois_cible")] string MoisCible,
    /// <summary>« AAAA-MM » du premier mois qui passe en négatif, ou null si ça passe.</summary>
    [property: JsonPropertyName("premier_mois_qui_casse")] string? PremierMoisQuiCasse,
    /// <summary>Ce qui manque au pire moment. Zéro si ça passe.</summary>
    [property: JsonPropertyName("manque_centimes")] long ManqueCentimes,
    IReadOnlyList<MoisProjeteDto> Nominale,
    IReadOnlyList<MoisProjeteDto> Simulee);

/// <summary>Erreur de validation renvoyée au client (lot rejeté, §6.2.2).</summary>
public sealed record ErreurChangementDto(
    [property: JsonPropertyName("change_id")] Guid ChangeId,
    IReadOnlyList<ErreurDetailDto> Erreurs);

public sealed record ErreurDetailDto(string Champ, string Code, string Message);
