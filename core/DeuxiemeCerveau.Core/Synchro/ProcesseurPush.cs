using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Temps;
using DeuxiemeCerveau.Core.Validation;

namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// Traitement d'un lot de push (§6.2) — NON NÉGOCIABLE, à l'identique dans les deux apps côté client :
/// 1. idempotence par change_id (un lot renvoyé après coupure ne s'applique jamais deux fois) ;
/// 2. atomicité par lot (entièrement ou pas du tout) ;
/// 3. arbitrage : dernière écriture gagne sur date_modification (UTC), égalité tranchée par
///    l'ordre d'arrivée serveur (premier arrivé gagne, D-005) — la version perdante est archivée au journal ;
/// 4. chaque changement appliqué reçoit un server_seq global strictement croissant ;
/// 5. fermeture de projet (§3.2) : cascade serveur déterministe et rejouable (D-006).
/// </summary>
public sealed class ProcesseurPush(IMagasinSynchro magasin, IHorloge horloge)
{
    public const int TailleLotMax = 500;

    public ReponsePush Traiter(LotPush lot)
    {
        if (lot.Changements.Count > TailleLotMax)
            throw new ErreurLotInvalide([
                new ErreurChangement(Guid.Empty,
                    [new ErreurValidation("changements", "lot_trop_grand",
                        $"Lot limité à {TailleLotMax} changements (D-009).")])
            ]);

        // ----- Phase 1 : validation intégrale — un seul changement invalide rejette le lot (§6.2.2). -----
        var prepares = new List<ChangementPrepare>(lot.Changements.Count);
        var erreurs = new List<ErreurChangement>();
        foreach (var changement in lot.Changements)
        {
            var erreursChangement = Preparer(changement, out var prepare);
            if (erreursChangement.Count > 0)
                erreurs.Add(new ErreurChangement(changement.ChangeId, erreursChangement));
            else
                prepares.Add(prepare!);
        }
        if (erreurs.Count > 0)
            throw new ErreurLotInvalide(erreurs);

        // ----- Phase 2 : application atomique, dans l'ordre du lot (§6.2). -----
        var resultats = new List<ResultatPush>(prepares.Count);
        var induits = new List<ChangementInduit>();
        magasin.DansTransaction(() =>
        {
            foreach (var prepare in prepares)
            {
                var resultat = AppliquerUn(prepare, induits);
                resultats.Add(resultat);
            }
        });

        return new ReponsePush(resultats, induits);
    }

    private sealed record ChangementPrepare(
        ChangementPush Changement,
        EntiteSynchronisee Entite);

    private IReadOnlyList<ErreurValidation> Preparer(ChangementPush changement, out ChangementPrepare? prepare)
    {
        prepare = null;
        var erreurs = new List<ErreurValidation>();

        if (changement.ChangeId == Guid.Empty)
            erreurs.Add(new("change_id", "change_id_manquant", "change_id (UUID local) obligatoire (§6.2)."));
        if (changement.AppareilId == Guid.Empty)
            erreurs.Add(new("appareil_id", "appareil_manquant", "appareil_id obligatoire (§6.2)."));

        EntiteSynchronisee entite;
        try
        {
            entite = AiguilleurEntites.Deserialiser(changement.Entite, changement.Payload);
        }
        catch (JsonException ex)
        {
            erreurs.Add(new("payload", "payload_invalide", $"Payload illisible : {ex.Message}"));
            return erreurs;
        }

        erreurs.AddRange(AiguilleurEntites.Valider(changement.Entite, entite));

        // Cohérence enveloppe ↔ payload : l'arbitrage se fait sur l'enveloppe, le payload est la vérité —
        // toute divergence est un bug client et rejette le lot.
        if (entite.Id != changement.EntiteId)
            erreurs.Add(new("element_id", "enveloppe_incoherente", "payload.id ≠ element_id de l'enveloppe."));
        if (entite.Version != changement.Version)
            erreurs.Add(new("version", "enveloppe_incoherente", "payload.version ≠ version de l'enveloppe."));
        if (entite.DateModification != changement.DateModification)
            erreurs.Add(new("date_modification", "enveloppe_incoherente",
                "payload.date_modification ≠ date_modification de l'enveloppe."));
        if (entite.AppareilSource != changement.AppareilId)
            erreurs.Add(new("appareil_source", "enveloppe_incoherente",
                "payload.appareil_source ≠ appareil_id de l'enveloppe."));

        if (erreurs.Count == 0)
            prepare = new ChangementPrepare(changement, entite);
        return erreurs;
    }

    private ResultatPush AppliquerUn(ChangementPrepare prepare, List<ChangementInduit> induits)
    {
        var changement = prepare.Changement;

        // Idempotence (§6.2.1, NON NÉGOCIABLE) : tout change_id déjà vu est ignoré silencieusement —
        // la réponse redonne le résultat d'origine, le client vide son outbox.
        if (magasin.JournalParChangeId(changement.ChangeId) is { } dejaVu)
            return new ResultatPush(changement.ChangeId, dejaVu.Resultat,
                Conflit: dejaVu.Resultat == ResultatChangement.PerdantArchive, dejaVu.ServerSeq, Rejoue: true);

        // Anti-résurrection (§5.6, D-010) : un changement retardataire visant une entité purgée est
        // refusé SANS archivage du payload — la destruction confirmée par l'utilisateur prime.
        // L'app abandonne l'entrée d'outbox et supprime définitivement sa copie locale.
        if (magasin.ObtenirTombale(changement.Entite, changement.EntiteId) is not null)
        {
            var seqRefus = magasin.AjouterJournal(new EntreeJournal(
                ServerSeq: 0, changement.ChangeId, changement.Entite, changement.EntiteId,
                ProcesseurPurge.MarqueurPurge, changement.AppareilId,
                ResultatChangement.RefusePurge, horloge.MaintenantUtc));
            return new ResultatPush(changement.ChangeId, ResultatChangement.RefusePurge,
                Conflit: false, seqRefus, Rejoue: false);
        }

        var courant = magasin.Obtenir(changement.Entite, changement.EntiteId);

        // Arbitrage (§6.2.3, D-005). Conflit si l'entité a été modifiée entre-temps (version non avancée).
        var conflit = courant is not null && changement.Version <= courant.Version;
        if (conflit)
        {
            var entrantGagne = changement.DateModification > courant!.DateModification;
            if (!entrantGagne)
            {
                // La version perdante (l'entrante) est archivée au journal — filet 3, rien ne se perd.
                var seqPerdant = magasin.AjouterJournal(new EntreeJournal(
                    ServerSeq: 0, changement.ChangeId, changement.Entite, changement.EntiteId,
                    Payload: changement.Payload.GetRawText(), changement.AppareilId,
                    ResultatChangement.PerdantArchive, horloge.MaintenantUtc));
                return new ResultatPush(changement.ChangeId, ResultatChangement.PerdantArchive,
                    Conflit: true, seqPerdant, Rejoue: false);
            }
        }

        // Application. Le compteur de version ne régresse jamais (D-005) ; en conflit gagné, il
        // dépasse la version courante pour que l'historique reste strictement croissant.
        var versionAppliquee = courant is null
            ? changement.Version
            : Math.Max(changement.Version, courant.Version + 1);
        var seq = Appliquer(prepare.Entite, changement.Entite, changement.ChangeId,
            changement.AppareilId, versionAppliquee);

        // Fermeture de projet (§3.2) : les tâches « a_faire » du projet passent « reporte ».
        if (prepare.Entite is Projet { Statut: StatutProjet.Termine or StatutProjet.EnPause } projet)
            Cascader(projet, changement, induits);

        return new ResultatPush(changement.ChangeId, ResultatChangement.Applique, conflit, seq, Rejoue: false);
    }

    /// <summary>Journalise puis écrit l'état canonique. Le payload du journal omet server_seq (porté par la colonne, D-005).</summary>
    private long Appliquer(EntiteSynchronisee entite, EntiteSynchro type, Guid changeId,
        Guid appareilId, int versionAppliquee)
    {
        entite.Version = versionAppliquee;
        entite.AppareilSource = appareilId;

        entite.ServerSeq = null;
        var payloadJournal = AiguilleurEntites.Serialiser(type, entite);
        var seq = magasin.AjouterJournal(new EntreeJournal(
            ServerSeq: 0, changeId, type, entite.Id, payloadJournal, appareilId,
            ResultatChangement.Applique, horloge.MaintenantUtc));

        entite.ServerSeq = seq;
        magasin.Ecrire(new EtatEntite(type, entite.Id, versionAppliquee, entite.DateModification,
            entite.Supprime, seq, AiguilleurEntites.Serialiser(type, entite)));
        return seq;
    }

    private void Cascader(Projet projet, ChangementPush declencheur, List<ChangementInduit> induits)
    {
        foreach (var etatTache in magasin.TachesAFaireDuProjet(projet.Id))
        {
            // change_id déterministe (UUIDv5) : le rejeu du lot retombe sur l'idempotence (D-006).
            var changeId = Uuid5.Derive($"cascade:{declencheur.ChangeId:D}:{etatTache.Id:D}");
            if (magasin.JournalParChangeId(changeId) is not null)
                continue;

            var tache = SerialisationCanonique.Deserialiser<Element>(etatTache.PayloadCanonique);
            tache.Statut = StatutElement.Reporte;
            tache.DateModification = declencheur.DateModification; // hérité du déclencheur (D-006)

            var seq = Appliquer(tache, EntiteSynchro.Element, changeId,
                declencheur.AppareilId, etatTache.Version + 1);
            induits.Add(new ChangementInduit(changeId, EntiteSynchro.Element, tache.Id, seq));
        }
    }
}
