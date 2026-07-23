namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// Abstraction de stockage du moteur de synchro. Le cœur décide, l'adaptateur stocke (règle 4) :
/// l'implémentation Azure SQL vit dans /api, l'implémentation mémoire de référence dans le cœur
/// (spécification exécutable + tests). Le moteur n'est pas thread-safe : l'adaptateur sérialise
/// les lots (D-005).
/// </summary>
public interface IMagasinSynchro
{
    EtatEntite? Obtenir(EntiteSynchro entite, Guid id);

    /// <summary>Crée ou remplace l'état courant d'une entité.</summary>
    void Ecrire(EtatEntite etat);

    /// <summary>Idempotence (§6.2.1) : entrée de journal déjà vue pour ce change_id, sinon null.</summary>
    EntreeJournal? JournalParChangeId(Guid changeId);

    /// <summary>
    /// Ajoute une entrée au journal en lui attribuant le prochain server_seq global strictement
    /// croissant (§6.2.4) — l'équivalent de l'IDENTITY de la table change_log.
    /// </summary>
    long AjouterJournal(EntreeJournal entree);

    /// <summary>Dernier server_seq attribué (0 si journal vide) — le curseur du pull.</summary>
    long SeqCourante { get; }

    /// <summary>États dont server_seq &gt; depuis, ordonnés par server_seq croissant, au plus limite.</summary>
    IReadOnlyList<EtatEntite> ModifiesDepuis(long depuis, int limite);

    /// <summary>Tâches « a_faire » non supprimées d'un projet — cascade de fermeture (§3.2).</summary>
    IReadOnlyList<EtatEntite> TachesAFaireDuProjet(Guid projetId);

    // ----- Purge (§5.6, D-010) — l'unique chemin de destruction réelle de l'application -----

    /// <summary>Pierre tombale d'une entité purgée, ou null.</summary>
    PierreTombale? ObtenirTombale(EntiteSynchro entite, Guid id);

    void AjouterTombale(PierreTombale tombale);

    /// <summary>Destruction réelle de l'état — appelé uniquement par le processeur de purge (§5.6).</summary>
    void SupprimerEtat(EntiteSynchro entite, Guid id);

    /// <summary>
    /// Remplace les payloads journalisés de l'entité par le marqueur de purge, en conservant les
    /// métadonnées (server_seq, change_id, resultat) — idempotence et séquences intactes (D-010).
    /// </summary>
    void CaviarderJournal(EntiteSynchro entite, Guid id, string marqueur);

    /// <summary>Tombales dont server_seq &gt; depuis, ordonnées par server_seq croissant, au plus limite.</summary>
    IReadOnlyList<PierreTombale> PurgesDepuis(long depuis, int limite);

    /// <summary>Pièces jointes (supprimées ou non) d'un Élément — cascade de purge (§7, D-010).</summary>
    IReadOnlyList<EtatEntite> PiecesJointesDeLElement(Guid elementId);

    /// <summary>Atomicité par lot (§6.2.2) : tout ou rien. Toute exception annule l'ensemble.</summary>
    void DansTransaction(Action action);
}
