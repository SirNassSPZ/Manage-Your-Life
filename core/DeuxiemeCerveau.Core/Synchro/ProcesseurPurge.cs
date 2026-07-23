using DeuxiemeCerveau.Core.Temps;
using DeuxiemeCerveau.Core.Validation;

namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// Purge définitive depuis la corbeille (§5.6, D-010, spec v3.2) — l'unique opération de
/// destruction réelle de l'application. Règles, dans l'ordre de priorité :
/// 1. la conservation gagne toute course : purge acceptée seulement si l'entité est encore
///    « supprime = true » à l'arrivée de la demande (restaurée entre-temps → refus) ;
/// 2. destruction réelle, protocole intact : état détruit, journal caviardé (métadonnées
///    conservées), pierre tombale posée, server_seq ordinaire transporté par le pull ;
/// 3. idempotence par change_id et atomicité de lot, comme le push (§6.2).
/// </summary>
public sealed class ProcesseurPurge(IMagasinSynchro magasin, IHorloge horloge)
{
    public const int TailleLotMax = 500;

    /// <summary>Marqueur remplaçant tout payload d'une entité purgée — la donnée est détruite (D-010).</summary>
    public const string MarqueurPurge = "{\"purge\":true}";

    public const string MotifRestauree = "restauree";
    public const string MotifInconnue = "inconnue";

    public ReponsePurge Traiter(LotPurge lot)
    {
        // ----- Validation intégrale — un lot invalide est rejeté en entier, rien n'est appliqué. -----
        var erreurs = new List<ErreurChangement>();
        if (lot.Purges.Count > TailleLotMax)
            erreurs.Add(new(Guid.Empty, [new ErreurValidation("purges", "lot_trop_grand",
                $"Lot limité à {TailleLotMax} purges.")]));
        if (lot.AppareilId == Guid.Empty)
            erreurs.Add(new(Guid.Empty, [new ErreurValidation("appareil_id", "appareil_manquant",
                "appareil_id obligatoire.")]));
        foreach (var demande in lot.Purges)
        {
            var erreursDemande = new List<ErreurValidation>();
            if (demande.ChangeId == Guid.Empty)
                erreursDemande.Add(new("change_id", "change_id_manquant", "change_id obligatoire (§6.2)."));
            if (demande.EntiteId == Guid.Empty)
                erreursDemande.Add(new("element_id", "id_manquant", "Identifiant d'entité obligatoire."));
            if (demande.Entite == EntiteSynchro.Reglage)
                erreursDemande.Add(new("entite", "entite_non_purgeable",
                    "Le réglage n'a pas de corbeille : il n'est pas purgeable (§5.6, D-010)."));
            if (erreursDemande.Count > 0)
                erreurs.Add(new(demande.ChangeId, erreursDemande));
        }
        if (erreurs.Count > 0)
            throw new ErreurLotInvalide(erreurs);

        // ----- Application atomique. Les refus sont des résultats, pas des erreurs (D-010). -----
        var resultats = new List<ResultatPurge>(lot.Purges.Count);
        var induites = new List<PurgeInduite>();
        magasin.DansTransaction(() =>
        {
            foreach (var demande in lot.Purges)
                resultats.Add(PurgerUne(demande, lot.AppareilId, induites));
        });

        return new ReponsePurge(resultats, induites);
    }

    private ResultatPurge PurgerUne(DemandePurge demande, Guid appareilId, List<PurgeInduite> induites)
    {
        // Idempotence : une demande déjà vue redonne son issue d'origine, sans réévaluation
        // (l'état a pu changer entre-temps — l'issue d'un change_id est définitive).
        if (magasin.JournalParChangeId(demande.ChangeId) is { } dejaVu)
            return new ResultatPurge(demande.ChangeId,
                dejaVu.Resultat == ResultatChangement.Purge ? StatutPurge.Purgee : StatutPurge.Refusee,
                dejaVu.ServerSeq, Rejoue: true, Motif: null);

        // Déjà purgée par une autre demande (deux appareils purgent la même entité) : succès de fait.
        if (magasin.ObtenirTombale(demande.Entite, demande.EntiteId) is not null)
        {
            var seqDeja = Journaliser(demande.ChangeId, demande.Entite, demande.EntiteId,
                appareilId, ResultatChangement.Purge);
            return new ResultatPurge(demande.ChangeId, StatutPurge.Purgee, seqDeja, Rejoue: false, Motif: null);
        }

        var etat = magasin.Obtenir(demande.Entite, demande.EntiteId);
        if (etat is null)
            return Refuser(demande, appareilId, MotifInconnue); // jamais de destruction à l'aveugle
        if (!etat.Supprime)
            return Refuser(demande, appareilId, MotifRestauree); // la conservation gagne (D-010.1)

        // Cascade : la purge d'un Élément purge ses pièces jointes (§7, D-010.4) —
        // change_id déterministes, donc rejouables sans double effet.
        if (demande.Entite == EntiteSynchro.Element)
        {
            foreach (var piece in magasin.PiecesJointesDeLElement(demande.EntiteId))
            {
                var changeInduit = Uuid5.Derive($"purge:{demande.ChangeId:D}:{piece.Id:D}");
                if (magasin.JournalParChangeId(changeInduit) is not null
                    || magasin.ObtenirTombale(EntiteSynchro.PieceJointe, piece.Id) is not null)
                    continue;
                var seqPiece = Purger(EntiteSynchro.PieceJointe, piece.Id, changeInduit, appareilId);
                induites.Add(new PurgeInduite(changeInduit, EntiteSynchro.PieceJointe, piece.Id, seqPiece));
            }
        }

        var seq = Purger(demande.Entite, demande.EntiteId, demande.ChangeId, appareilId);
        return new ResultatPurge(demande.ChangeId, StatutPurge.Purgee, seq, Rejoue: false, Motif: null);
    }

    private long Purger(EntiteSynchro entite, Guid id, Guid changeId, Guid appareilId)
    {
        magasin.SupprimerEtat(entite, id);                       // destruction réelle de l'état
        magasin.CaviarderJournal(entite, id, MarqueurPurge);     // payloads détruits, métadonnées gardées
        var seq = Journaliser(changeId, entite, id, appareilId, ResultatChangement.Purge);
        magasin.AjouterTombale(new PierreTombale(entite, id, seq, changeId, appareilId, horloge.MaintenantUtc));
        return seq;
    }

    private ResultatPurge Refuser(DemandePurge demande, Guid appareilId, string motif)
    {
        // Journalisé pour l'idempotence : un rejeu de la demande redonnera « refusée ».
        var seq = Journaliser(demande.ChangeId, demande.Entite, demande.EntiteId,
            appareilId, ResultatChangement.RefusePurge);
        return new ResultatPurge(demande.ChangeId, StatutPurge.Refusee, seq, Rejoue: false, motif);
    }

    private long Journaliser(Guid changeId, EntiteSynchro entite, Guid id, Guid appareilId,
        ResultatChangement resultat)
        => magasin.AjouterJournal(new EntreeJournal(
            ServerSeq: 0, changeId, entite, id, MarqueurPurge, appareilId, resultat, horloge.MaintenantUtc));
}
