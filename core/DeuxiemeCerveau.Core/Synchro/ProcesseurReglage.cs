using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>Requête de recalage du solde de référence (§3.4) — `PUT /settings/solde-reference` (§8).</summary>
public sealed class RecalageSolde
{
    /// <summary>Idempotence du PUT — généré par l'appareil, comme tout changement (§6.2).</summary>
    public Guid ChangeId { get; set; }

    public long SoldeReferenceCentimes { get; set; }

    public DateOnly SoldeReferenceDate { get; set; }

    public DateTimeOffset DateModification { get; set; }

    public Guid AppareilId { get; set; }
}

/// <summary>
/// Recalage du solde de référence (§3.4) : « dernier recalage gagne, historique conservé au journal ».
/// Passe par le même chemin d'arbitrage que tout changement (entité « reglage », D-006) —
/// une seule mécanique de synchro, écrite une seule fois.
/// </summary>
public sealed class ProcesseurReglage(IMagasinSynchro magasin, IHorloge horloge)
{
    public ResultatPush Recaler(RecalageSolde recalage)
    {
        var courant = magasin.Obtenir(EntiteSynchro.Reglage, ReglageSolde.IdSoldeReference);

        // « Dernier recalage gagne » (§3.4) se juge sur date_modification. Le PUT ne porte pas de
        // version : elle est posée ici. Un recalage plus récent avance la version (application
        // directe) ; un recalage plus ancien ou simultané garde la version courante, ce qui déclenche
        // l'arbitrage du moteur → perdant archivé au journal (historique conservé).
        var version = courant is null
            ? 1
            : recalage.DateModification > courant.DateModification
                ? courant.Version + 1
                : courant.Version;

        var reglage = new ReglageSolde
        {
            Id = ReglageSolde.IdSoldeReference,
            SoldeReferenceCentimes = recalage.SoldeReferenceCentimes,
            SoldeReferenceDate = recalage.SoldeReferenceDate,
            DateCreation = courant is null
                ? recalage.DateModification
                : SerialisationCanonique.Deserialiser<ReglageSolde>(courant.PayloadCanonique).DateCreation,
            DateModification = recalage.DateModification,
            AppareilSource = recalage.AppareilId,
            Version = version,
        };

        var lot = new LotPush
        {
            AppareilId = recalage.AppareilId,
            Changements =
            {
                new ChangementPush
                {
                    ChangeId = recalage.ChangeId,
                    Entite = EntiteSynchro.Reglage,
                    EntiteId = reglage.Id,
                    Version = reglage.Version,
                    DateModification = reglage.DateModification,
                    AppareilId = recalage.AppareilId,
                    Payload = JsonSerializer.SerializeToElement(reglage, SerialisationCanonique.Options),
                },
            },
        };

        var reponse = new ProcesseurPush(magasin, horloge).Traiter(lot);
        return reponse.Resultats[0];
    }

    /// <summary>Le solde de référence courant, ou null s'il n'a jamais été saisi.</summary>
    public ReglageSolde? Lire()
        => magasin.Obtenir(EntiteSynchro.Reglage, ReglageSolde.IdSoldeReference) is { } etat && !etat.Supprime
            ? SerialisationCanonique.Deserialiser<ReglageSolde>(etat.PayloadCanonique)
            : null;
}
