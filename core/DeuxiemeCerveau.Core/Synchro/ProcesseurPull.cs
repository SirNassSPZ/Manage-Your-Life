namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// Pull par curseur (§6.2) : renvoie les entités modifiées depuis « since » et le nouveau curseur.
/// La reprise après coupure est automatique : le curseur est le point de reprise.
/// </summary>
public sealed class ProcesseurPull(IMagasinSynchro magasin)
{
    public const int LimiteParDefaut = 500;

    public PagePull Traiter(long depuis, int limite = LimiteParDefaut)
    {
        if (limite < 1)
            throw new ArgumentOutOfRangeException(nameof(limite));

        var entites = magasin.ModifiesDepuis(depuis, limite + 1);
        var encore = entites.Count > limite;
        if (encore)
            entites = entites.Take(limite).ToList();

        // Page pleine : curseur = dernier server_seq renvoyé (reprise exacte).
        // Page complète : curseur = séquence courante (absorbe les seq « perdant_archive »,
        // qui ne modifient aucun état et n'ont donc rien à renvoyer).
        var curseur = encore
            ? entites[^1].ServerSeq
            : Math.Max(depuis, magasin.SeqCourante);

        return new PagePull(entites, curseur, encore);
    }
}
