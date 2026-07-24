using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Windows.Core.Synchro;

/// <summary>
/// Entrée d'outbox persistante (§6.2) : conserve toute modification locale avant envoi serveur.
/// Survit au redémarrage de l'application.
/// </summary>
public sealed class EntreeOutbox
{
    public Guid ChangeId { get; set; }

    public EntiteSynchro Entite { get; set; }

    public Guid EntiteId { get; set; }

    public int Version { get; set; }

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset DateModification { get; set; }

    public Guid AppareilId { get; set; }

    public DateTimeOffset CreeLe { get; set; } = DateTimeOffset.UtcNow;

    public ChangementPush VersChangementPush() => new()
    {
        ChangeId = ChangeId,
        Entite = Entite,
        EntiteId = EntiteId,
        Version = Version,
        DateModification = DateModification,
        AppareilId = AppareilId,
        Payload = System.Text.Json.JsonDocument.Parse(Payload).RootElement.Clone()
    };
}
