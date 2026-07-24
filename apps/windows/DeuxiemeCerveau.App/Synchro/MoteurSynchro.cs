using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Synchro;

/// <summary>
/// Moteur de synchronisation local-first (§6.2) — le morceau le plus délicat du projet, dérivé de la
/// spec mot pour mot. Cycle : enregistrement (une fois) → <b>push</b> (vider l'outbox par lots
/// ordonnés, retirer les change_id confirmés) → <b>pull</b> (appliquer entités et purges par curseur).
/// Idempotence et curseur rendent toute coupure réseau inoffensive : rejouer un lot ne l'applique pas
/// deux fois, et le curseur est le point de reprise. server_seq est toujours posé par le serveur.
/// </summary>
public sealed class MoteurSynchro(DepotLocal depot, IdentiteAppareil identite, IClientApi api)
{
    public const string CleCurseur = "curseur_pull";
    public const string CleEnregistre = "appareil_enregistre";

    /// <summary>Lot de push ≤ limite serveur (500, D-009) — on reste prudemment en dessous.</summary>
    public const int TailleLot = 200;

    /// <summary>Un cycle complet (§6.2 « Cycle. Push puis pull ») : enregistrement si besoin, push, pull.</summary>
    public async Task Synchroniser(string nomAppareil, string plateforme, CancellationToken jeton = default)
    {
        await AssurerEnregistrement(nomAppareil, plateforme, jeton);
        await Pousser(jeton);
        await Tirer(jeton);
    }

    /// <summary>
    /// Enregistrement à la première connexion (§6.2) : le client adopte l'appareil_id renvoyé par le
    /// serveur (D-015). L'id local provisoire (généré pour la saisie hors-ligne, filet 1) reste porté
    /// par les entités déjà créées — un simple marqueur de provenance, sans effet sur l'arbitrage.
    /// </summary>
    public async Task AssurerEnregistrement(string nom, string plateforme, CancellationToken jeton = default)
    {
        if (depot.LireEtat(CleEnregistre) == "true")
            return;
        var idServeur = await api.EnregistrerAppareil(nom, plateforme, jeton);
        depot.EcrireEtat(IdentiteAppareil.Cle, idServeur.ToString());
        depot.EcrireEtat(CleEnregistre, "true");
    }

    /// <summary>
    /// Push (§6.2) : envoie l'outbox par lots ordonnés, puis retire les change_id confirmés (étape 5).
    /// L'idempotence serveur rend un renvoi après coupure sans danger (même server_seq, aucun doublon).
    /// Un changement refusé pour cause de purge (anti-résurrection, D-010) est abandonné et la copie
    /// locale détruite.
    /// </summary>
    public async Task Pousser(CancellationToken jeton = default)
    {
        while (true)
        {
            var enAttente = depot.Outbox();
            if (enAttente.Count == 0)
                return;

            var changements = enAttente.Take(TailleLot).ToList();
            var lot = new LotPush { AppareilId = identite.Obtenir(), Changements = changements };
            var reponse = await api.Pousser(lot, jeton);

            foreach (var resultat in reponse.Resultats)
            {
                if (resultat.Resultat == ResultatChangement.RefusePurge)
                {
                    var chg = changements.First(c => c.ChangeId == resultat.ChangeId);
                    depot.DansTransaction(() =>
                    {
                        depot.SupprimerReel(chg.Entite, chg.EntiteId);   // la copie locale est détruite (§5.6)
                        depot.ViderOutboxEntite(chg.Entite, chg.EntiteId); // et les changements en attente abandonnés
                    });
                }
                else
                {
                    depot.RetirerOutbox(resultat.ChangeId); // confirmé (appliqué, perdant archivé, ou rejoué)
                }
            }
        }
    }

    /// <summary>
    /// Pull (§6.2) : demande les changements depuis le curseur, applique entités et purges en local,
    /// puis avance le curseur — le tout atomiquement. La reprise après coupure est automatique : le
    /// curseur EST le point de reprise.
    /// </summary>
    public async Task Tirer(CancellationToken jeton = default)
    {
        while (true)
        {
            var page = await api.Tirer(LireCurseur(), TailleLot, jeton);
            depot.DansTransaction(() =>
            {
                foreach (var entite in page.Entites)
                    depot.Ecrire(new EtatEntite(entite.Entite, entite.Id, entite.Version,
                        entite.DateModification, entite.Supprime, entite.ServerSeq, entite.Payload.GetRawText()));
                foreach (var purge in page.Purges)
                {
                    // Le pull transporte les purges (§5.6) : destruction locale + abandon des changements en attente.
                    depot.SupprimerReel(purge.Entite, purge.Id);
                    depot.ViderOutboxEntite(purge.Entite, purge.Id);
                }
                depot.EcrireEtat(CleCurseur, page.Curseur.ToString());
            });
            if (!page.Encore)
                return;
        }
    }

    private long LireCurseur()
        => depot.LireEtat(CleCurseur) is { } v && long.TryParse(v, out var curseur) ? curseur : 0;
}
