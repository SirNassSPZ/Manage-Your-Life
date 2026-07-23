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

    /// <summary>Atomicité par lot (§6.2.2) : tout ou rien. Toute exception annule l'ensemble.</summary>
    void DansTransaction(Action action);
}
