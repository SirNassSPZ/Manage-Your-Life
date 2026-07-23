using System.Text.Json;

namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// Entités portées par le moteur de synchro (D-006) — mêmes champs d'audit, même arbitrage,
/// écrit une seule fois. JSON : « element », « categorie », « projet », « budget », « piece_jointe », « reglage ».
/// </summary>
public enum EntiteSynchro
{
    Element,
    Categorie,
    Projet,
    Budget,
    PieceJointe,
    Reglage,
}

/// <summary>
/// Entrée d'outbox poussée par un appareil (§6.2) :
/// « { change_id, element_id, version, payload complet, date_modification, appareil_id } »,
/// généralisée à toutes les entités synchronisées (D-006).
/// </summary>
public sealed class ChangementPush
{
    public Guid ChangeId { get; set; }

    public EntiteSynchro Entite { get; set; } = EntiteSynchro.Element;

    /// <summary>Identifiant de l'entité — nom de fil « element_id », fidèle au §6.2 (généralisé D-006).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("element_id")]
    public Guid EntiteId { get; set; }

    /// <summary>Version produite par l'appareil (compteur croissant de l'entité).</summary>
    public int Version { get; set; }

    /// <summary>UTC — base de l'arbitrage (dernière écriture gagne, §6.2.3).</summary>
    public DateTimeOffset DateModification { get; set; }

    public Guid AppareilId { get; set; }

    /// <summary>Payload complet de l'entité (JSON canonique, D-007).</summary>
    public JsonElement Payload { get; set; }
}

/// <summary>Lot ordonné (§6.2) : s'applique entièrement ou pas du tout.</summary>
public sealed class LotPush
{
    public Guid AppareilId { get; set; }

    public List<ChangementPush> Changements { get; set; } = new();
}

/// <summary>
/// Résultat serveur d'un changement au journal (§6.2.4, §5.6) : appliqué, perdant archivé (filet 3),
/// purge (D-010), ou refus d'un changement visant une entité purgée (anti-résurrection, D-010).
/// </summary>
public enum ResultatChangement
{
    Applique,
    PerdantArchive,
    Purge,
    RefusePurge,
}

public sealed record ResultatPush(
    Guid ChangeId,
    ResultatChangement Resultat,
    bool Conflit,
    long ServerSeq,
    bool Rejoue);

/// <summary>Changement induit par le serveur (cascade de fermeture de projet, §3.2, D-006).</summary>
public sealed record ChangementInduit(Guid ChangeId, EntiteSynchro Entite, Guid EntiteId, long ServerSeq);

public sealed record ReponsePush(
    IReadOnlyList<ResultatPush> Resultats,
    IReadOnlyList<ChangementInduit> ChangementsInduits);

/// <summary>État courant d'une entité côté serveur — payload canonique + champs d'arbitrage.</summary>
public sealed record EtatEntite(
    EntiteSynchro Entite,
    Guid Id,
    int Version,
    DateTimeOffset DateModification,
    bool Supprime,
    long ServerSeq,
    string PayloadCanonique);

/// <summary>
/// Entrée du journal des changements (§6.2.4) : l'archive des perdants (filet 3) et la trace de
/// toute écriture. Payload : canonique appliqué pour « applique », reçu tel quel pour « perdant_archive » (D-005).
/// </summary>
public sealed record EntreeJournal(
    long ServerSeq,
    Guid ChangeId,
    EntiteSynchro Entite,
    Guid EntiteId,
    string Payload,
    Guid AppareilId,
    ResultatChangement Resultat,
    DateTimeOffset RecuLe);

/// <summary>Page de pull (§6.2) : entités modifiées et purges (§5.6) depuis le curseur + nouveau curseur.</summary>
public sealed record PagePull(
    IReadOnlyList<EtatEntite> Entites,
    IReadOnlyList<PierreTombale> Purges,
    long Curseur,
    bool Encore);

/// <summary>
/// Pierre tombale d'une purge (§5.6, D-010) : mémoire durable qu'une entité a été détruite —
/// transportée par le pull, elle interdit toute résurrection par un appareil retardataire.
/// </summary>
public sealed record PierreTombale(
    EntiteSynchro Entite,
    Guid Id,
    long ServerSeq,
    Guid ChangeId,
    Guid AppareilId,
    DateTimeOffset PurgeLe);

/// <summary>Demande de purge d'une entité de la corbeille (§5.6). Nom de fil « element_id » comme au §6.2.</summary>
public sealed class DemandePurge
{
    public Guid ChangeId { get; set; }

    public EntiteSynchro Entite { get; set; } = EntiteSynchro.Element;

    [System.Text.Json.Serialization.JsonPropertyName("element_id")]
    public Guid EntiteId { get; set; }
}

/// <summary>Lot de purge (`POST /purge`, §8) : idempotent par change_id, atomique pour les erreurs de validation.</summary>
public sealed class LotPurge
{
    public Guid AppareilId { get; set; }

    public List<DemandePurge> Purges { get; set; } = new();
}

/// <summary>Issue d'une demande de purge : purgée, ou refusée (les refus sont des résultats, pas des erreurs).</summary>
public enum StatutPurge
{
    Purgee,
    Refusee,
}

public sealed record ResultatPurge(
    Guid ChangeId,
    StatutPurge Statut,
    long ServerSeq,
    bool Rejoue,
    string? Motif);

/// <summary>Purge induite par cascade (pièces jointes d'un Élément purgé, §7, D-010).</summary>
public sealed record PurgeInduite(Guid ChangeId, EntiteSynchro Entite, Guid EntiteId, long ServerSeq);

public sealed record ReponsePurge(
    IReadOnlyList<ResultatPurge> Resultats,
    IReadOnlyList<PurgeInduite> PurgesInduites);

/// <summary>Erreurs de validation d'un changement — le lot entier est rejeté (atomicité, §6.2.2).</summary>
public sealed record ErreurChangement(Guid ChangeId, IReadOnlyList<Validation.ErreurValidation> Erreurs);

public sealed class ErreurLotInvalide(IReadOnlyList<ErreurChangement> erreurs)
    : Exception($"Lot rejeté : {erreurs.Count} changement(s) invalide(s) — rien n'a été appliqué (§6.2.2).")
{
    public IReadOnlyList<ErreurChangement> Erreurs { get; } = erreurs;
}
