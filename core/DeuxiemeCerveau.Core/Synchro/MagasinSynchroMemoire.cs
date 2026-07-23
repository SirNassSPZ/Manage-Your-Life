using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// Implémentation mémoire de référence du magasin de synchro — spécification exécutable du contrat
/// que l'adaptateur Azure SQL (Étape 3) et les bases locales devront respecter. Sans dépendance (règle 4).
/// </summary>
public sealed class MagasinSynchroMemoire : IMagasinSynchro
{
    private readonly Dictionary<(EntiteSynchro, Guid), EtatEntite> _etats = new();
    private readonly List<EntreeJournal> _journal = new();
    private readonly Dictionary<Guid, EntreeJournal> _journalParChangeId = new();
    private long _seq;

    public IReadOnlyList<EntreeJournal> Journal => _journal;

    public EtatEntite? Obtenir(EntiteSynchro entite, Guid id)
        => _etats.GetValueOrDefault((entite, id));

    public void Ecrire(EtatEntite etat)
        => _etats[(etat.Entite, etat.Id)] = etat;

    public EntreeJournal? JournalParChangeId(Guid changeId)
        => _journalParChangeId.GetValueOrDefault(changeId);

    public long AjouterJournal(EntreeJournal entree)
    {
        if (_journalParChangeId.ContainsKey(entree.ChangeId))
            throw new InvalidOperationException(
                $"change_id déjà journalisé : {entree.ChangeId} (idempotence violée en amont).");
        var seq = ++_seq;
        var enregistree = entree with { ServerSeq = seq };
        _journal.Add(enregistree);
        _journalParChangeId[entree.ChangeId] = enregistree;
        return seq;
    }

    public long SeqCourante => _seq;

    public IReadOnlyList<EtatEntite> ModifiesDepuis(long depuis, int limite)
        => _etats.Values
            .Where(e => e.ServerSeq > depuis)
            .OrderBy(e => e.ServerSeq)
            .Take(limite)
            .ToList();

    public IReadOnlyList<EtatEntite> TachesAFaireDuProjet(Guid projetId)
        => _etats.Values
            .Where(e => e.Entite == EntiteSynchro.Element && !e.Supprime)
            .Where(e =>
            {
                var element = SerialisationCanonique.Deserialiser<Element>(e.PayloadCanonique);
                return element.Type == TypeElement.Tache
                    && element.Statut == StatutElement.AFaire
                    && element.ProjetId == projetId;
            })
            .OrderBy(e => e.ServerSeq) // ordre déterministe — même cascade partout
            .ToList();

    public void DansTransaction(Action action)
    {
        // Tout ou rien (§6.2.2) : instantané avant, restauration en cas d'échec.
        var etatsAvant = new Dictionary<(EntiteSynchro, Guid), EtatEntite>(_etats);
        var journalAvant = _journal.Count;
        var seqAvant = _seq;
        try
        {
            action();
        }
        catch
        {
            _etats.Clear();
            foreach (var (cle, valeur) in etatsAvant)
                _etats[cle] = valeur;
            for (var i = _journal.Count - 1; i >= journalAvant; i--)
            {
                _journalParChangeId.Remove(_journal[i].ChangeId);
                _journal.RemoveAt(i);
            }
            _seq = seqAvant;
            throw;
        }
    }
}
