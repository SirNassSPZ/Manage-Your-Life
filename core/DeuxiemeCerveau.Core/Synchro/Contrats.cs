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

/// <summary>Résultat serveur d'un changement : « applique » ou « perdant_archive » (§6.2.4).</summary>
public enum ResultatChangement
{
    Applique,
    PerdantArchive,
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

/// <summary>Page de pull (§6.2) : entités modifiées depuis le curseur + nouveau curseur.</summary>
public sealed record PagePull(
    IReadOnlyList<EtatEntite> Entites,
    long Curseur,
    bool Encore);

/// <summary>Erreurs de validation d'un changement — le lot entier est rejeté (atomicité, §6.2.2).</summary>
public sealed record ErreurChangement(Guid ChangeId, IReadOnlyList<Validation.ErreurValidation> Erreurs);

public sealed class ErreurLotInvalide(IReadOnlyList<ErreurChangement> erreurs)
    : Exception($"Lot rejeté : {erreurs.Count} changement(s) invalide(s) — rien n'a été appliqué (§6.2.2).")
{
    public IReadOnlyList<ErreurChangement> Erreurs { get; } = erreurs;
}
