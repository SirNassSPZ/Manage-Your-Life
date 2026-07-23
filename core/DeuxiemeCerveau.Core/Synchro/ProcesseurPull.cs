namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// Pull par curseur (§6.2) : renvoie les entités modifiées et les purges (§5.6) depuis « since »,
/// plus le nouveau curseur. La reprise après coupure est automatique : le curseur est le point de reprise.
/// </summary>
public sealed class ProcesseurPull(IMagasinSynchro magasin)
{
    public const int LimiteParDefaut = 500;

    public PagePull Traiter(long depuis, int limite = LimiteParDefaut)
    {
        if (limite < 1)
            throw new ArgumentOutOfRangeException(nameof(limite));

        // Fusion ordonnée par server_seq : états modifiés et pierres tombales partagent la même
        // séquence globale — un seul curseur couvre les deux (D-010).
        var fusion = magasin.ModifiesDepuis(depuis, limite + 1)
            .Select(e => (Seq: e.ServerSeq, Etat: (EtatEntite?)e, Tombale: (PierreTombale?)null))
            .Concat(magasin.PurgesDepuis(depuis, limite + 1)
                .Select(t => (Seq: t.ServerSeq, Etat: (EtatEntite?)null, Tombale: (PierreTombale?)t)))
            .OrderBy(x => x.Seq)
            .ToList();

        var encore = fusion.Count > limite;
        var page = encore ? fusion.Take(limite).ToList() : fusion;

        // Page pleine : curseur = dernier server_seq renvoyé (reprise exacte).
        // Page complète : curseur = séquence courante (absorbe les seq sans effet d'état :
        // perdants archivés, refus de purge).
        var curseur = encore
            ? page[^1].Seq
            : Math.Max(depuis, magasin.SeqCourante);

        return new PagePull(
            page.Where(x => x.Etat is not null).Select(x => x.Etat!).ToList(),
            page.Where(x => x.Tombale is not null).Select(x => x.Tombale!).ToList(),
            curseur, encore);
    }
}
