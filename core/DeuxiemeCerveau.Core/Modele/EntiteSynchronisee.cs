namespace DeuxiemeCerveau.Core.Modele;

/// <summary>
/// Champs d'identité et d'audit/synchro communs à toutes les entités synchronisées (§3.1, NON NÉGOCIABLES).
/// <c>server_seq</c> est posé par le serveur, jamais par le client.
/// </summary>
public abstract class EntiteSynchronisee
{
    public Guid Id { get; set; }

    /// <summary>UTC, posée par l'appareil à la création.</summary>
    public DateTimeOffset DateCreation { get; set; }

    /// <summary>UTC, posée par l'appareil à chaque modification — base de l'arbitrage des conflits (§6.2).</summary>
    public DateTimeOffset DateModification { get; set; }

    /// <summary>Appareil ayant fait la dernière modification.</summary>
    public Guid AppareilSource { get; set; }

    /// <summary>Compteur croissant, incrémenté à chaque modification.</summary>
    public int Version { get; set; }

    /// <summary>Numéro de séquence global posé par le serveur (curseur du pull). Null tant que jamais poussé.</summary>
    public long? ServerSeq { get; set; }

    /// <summary>Une suppression est un marquage, jamais une destruction (filet 2, §6.1).</summary>
    public bool Supprime { get; set; }

    public DateTimeOffset? DateSuppression { get; set; }
}
