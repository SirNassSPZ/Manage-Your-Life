using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// Implémentation mémoire de référence du magasin de synchro — spécification exécutable du contrat
/// que l'adaptateur Azure SQL (Étape 3) et les bases locales devront respecter. Sans dépendance (règle 4).
/// </summary>
public sealed class MagasinSynchroMemoire : IMagasinSynchro
{
    private Dictionary<(EntiteSynchro, Guid), EtatEntite> _etats = new();
    private List<EntreeJournal> _journal = new();
    private Dictionary<Guid, EntreeJournal> _journalParChangeId = new();
    private Dictionary<(EntiteSynchro, Guid), PierreTombale> _tombales = new();
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

    // ----- Purge (§5.6, D-010) -----

    public PierreTombale? ObtenirTombale(EntiteSynchro entite, Guid id)
        => _tombales.GetValueOrDefault((entite, id));

    public void AjouterTombale(PierreTombale tombale)
        => _tombales[(tombale.Entite, tombale.Id)] = tombale;

    public void SupprimerEtat(EntiteSynchro entite, Guid id)
        => _etats.Remove((entite, id));

    public void CaviarderJournal(EntiteSynchro entite, Guid id, string marqueur)
    {
        for (var i = 0; i < _journal.Count; i++)
        {
            var entree = _journal[i];
            if (entree.Entite != entite || entree.EntiteId != id || entree.Payload == marqueur)
                continue;
            var caviardee = entree with { Payload = marqueur };
            _journal[i] = caviardee;
            _journalParChangeId[entree.ChangeId] = caviardee;
        }
    }

    public IReadOnlyList<PierreTombale> PurgesDepuis(long depuis, int limite)
        => _tombales.Values
            .Where(t => t.ServerSeq > depuis)
            .OrderBy(t => t.ServerSeq)
            .Take(limite)
            .ToList();

    public IReadOnlyList<EtatEntite> PiecesJointesDeLElement(Guid elementId)
        => _etats.Values
            .Where(e => e.Entite == EntiteSynchro.PieceJointe)
            .Where(e => SerialisationCanonique.Deserialiser<PieceJointe>(e.PayloadCanonique).ElementId == elementId)
            .OrderBy(e => e.ServerSeq)
            .ToList();

    public void DansTransaction(Action action)
    {
        // Tout ou rien (§6.2.2) : instantané complet avant, restauration intégrale en cas d'échec
        // (le caviardage de purge modifie des entrées de journal existantes — la copie superficielle
        // suffit, les enregistrements étant immuables).
        var etatsAvant = new Dictionary<(EntiteSynchro, Guid), EtatEntite>(_etats);
        var journalAvant = new List<EntreeJournal>(_journal);
        var journalParChangeIdAvant = new Dictionary<Guid, EntreeJournal>(_journalParChangeId);
        var tombalesAvant = new Dictionary<(EntiteSynchro, Guid), PierreTombale>(_tombales);
        var seqAvant = _seq;
        try
        {
            action();
        }
        catch
        {
            _etats = etatsAvant;
            _journal = journalAvant;
            _journalParChangeId = journalParChangeIdAvant;
            _tombales = tombalesAvant;
            _seq = seqAvant;
            throw;
        }
    }
}
