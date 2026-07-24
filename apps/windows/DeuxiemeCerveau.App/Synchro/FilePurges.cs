using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Synchro;

/// <summary>Une demande de purge en attente d'envoi au serveur (§5.6).</summary>
public sealed record PurgeEnAttente(Guid ChangeId, EntiteSynchro Entite, Guid EntiteId);

/// <summary>
/// File persistante des purges à envoyer (§5.6) — l'équivalent de l'outbox pour les purges, afin que la
/// demande survive à une coupure ou à un redémarrage. Rangée dans <c>sync_etat</c> (opération rare,
/// petite liste). Idempotente côté serveur par <c>change_id</c>.
/// </summary>
public sealed class FilePurges(DepotLocal depot)
{
    public const string Cle = "purges_en_attente";

    public IReadOnlyList<PurgeEnAttente> Lister()
        => depot.LireEtat(Cle) is { } json ? SerialisationCanonique.Deserialiser<List<PurgeEnAttente>>(json) : [];

    public void Ajouter(PurgeEnAttente purge)
    {
        var liste = Lister().ToList();
        liste.Add(purge);
        depot.EcrireEtat(Cle, SerialisationCanonique.Serialiser(liste));
    }

    public void Retirer(IEnumerable<Guid> changeIds)
    {
        var traites = changeIds.ToHashSet();
        var reste = Lister().Where(p => !traites.Contains(p.ChangeId)).ToList();
        depot.EcrireEtat(Cle, SerialisationCanonique.Serialiser(reste));
    }
}
