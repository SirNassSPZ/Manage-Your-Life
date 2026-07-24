using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Validation;

namespace DeuxiemeCerveau.App.Services;

/// <summary>Bilan d'une confirmation en lot (reco #7) : ce qui a été confirmé, et ce qui a été refusé et pourquoi.</summary>
public sealed record ResultatConfirmationLot(
    IReadOnlyList<Guid> Confirmes,
    IReadOnlyList<(Guid Id, string Motif)> Refuses);

/// <summary>
/// Confirmation « payé / reçu » (feuille de route Palier 1 rang 4 / reco #7, décision D-017 Option A).
/// Marquer une échéance réglée, ou un revenu arrivé, EST un changement de statut de l'Élément (§3.1) :
/// `a_venir` → `paye`, `attendu` → `recu`. Passe par la saisie locale-d'abord (§6) : confirmé hors-ligne,
/// synchronisé ensuite ; alimente le suivi d'enveloppe (§3.6 : « dépensé = occurrences paye »).
///
/// <para><b>Périmètre V1 (D-017).</b> La confirmation s'applique aux Éléments <b>ponctuels</b>. Un Élément
/// <b>récurrent</b> est refusé (un statut unique vaudrait pour TOUTES ses occurrences) : en V1 un écart
/// réel se corrige par le <b>recalage du solde</b> (§3.4) ; le pointage par occurrence est prévu V2.</para>
/// </summary>
public sealed class ServiceConfirmation(DepotLocal depot, ServiceSaisie saisie)
{
    /// <summary>Confirme un Élément ponctuel (payé/reçu), ou rejette avec un motif clair.</summary>
    public ResultatSaisie Confirmer(Guid elementId)
    {
        var etat = depot.Obtenir(EntiteSynchro.Element, elementId);
        if (etat is null)
            return ResultatSaisie.Rejete([new ErreurValidation("id", "introuvable", "Élément inconnu en local.")]);
        var e = SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);

        if (!string.IsNullOrWhiteSpace(e.Recurrence))
            return ResultatSaisie.Rejete([new ErreurValidation("recurrence", "recurrent_non_confirmable",
                "Une échéance récurrente ne se coche pas à l'unité en V1 — recale ton solde pour un ajustement (D-017).")]);

        var cible = Cible(e);
        if (cible is null)
            return ResultatSaisie.Rejete([new ErreurValidation("statut", "non_confirmable",
                "Seuls une facture/un paiement « à venir » ou un revenu « attendu » se confirment.")]);

        e.Statut = cible.Value;
        return saisie.Enregistrer(e, EntiteSynchro.Element); // audit (version++), validation, local + outbox
    }

    /// <summary>Confirme plusieurs Éléments d'un coup (reco #7, sélection multiple) — chacun jugé indépendamment.</summary>
    public ResultatConfirmationLot ConfirmerLot(IEnumerable<Guid> elementIds)
    {
        var confirmes = new List<Guid>();
        var refuses = new List<(Guid, string)>();
        foreach (var id in elementIds.Distinct())
        {
            var r = Confirmer(id);
            if (r.Reussi)
                confirmes.Add(id);
            else
                refuses.Add((id, r.Erreurs.FirstOrDefault()?.Message ?? "Refusé."));
        }
        return new ResultatConfirmationLot(confirmes, refuses);
    }

    /// <summary>
    /// Les Éléments confirmables (ponctuels, financiers, au bon statut), triés par échéance. Si
    /// <paramref name="echusAu"/> est fourni, seulement ceux dont l'échéance est atteinte — pour proposer
    /// « confirmer tout ce qui est dû ».
    /// </summary>
    public IReadOnlyList<Element> AConfirmer(DateTimeOffset? echusAu = null)
        => Elements()
            .Where(e => !e.Supprime)
            .Where(EstConfirmable)
            .Where(e => echusAu is null || (e.DateDebut is not null && e.DateDebut <= echusAu))
            .OrderBy(e => e.DateDebut ?? DateTimeOffset.MaxValue)
            .ToList();

    private static bool EstConfirmable(Element e)
        => string.IsNullOrWhiteSpace(e.Recurrence) && Cible(e) is not null;

    /// <summary>Le statut « confirmé » cible selon le type et le statut courant, ou null si non confirmable.</summary>
    private static StatutElement? Cible(Element e) => e switch
    {
        { Type: TypeElement.Facture or TypeElement.Paiement, Statut: StatutElement.AVenir } => StatutElement.Paye,
        { Type: TypeElement.Revenu, Statut: StatutElement.Attendu } => StatutElement.Recu,
        _ => null,
    };

    private IEnumerable<Element> Elements()
        => depot.Enumerer(EntiteSynchro.Element)
            .Select(e => SerialisationCanonique.Deserialiser<Element>(e.PayloadCanonique));
}
