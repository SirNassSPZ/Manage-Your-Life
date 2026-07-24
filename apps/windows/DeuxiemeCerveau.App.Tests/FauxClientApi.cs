using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Faux client d'API adossé au VRAI serveur (<see cref="ServiceApi"/> + magasin mémoire) : aller-retour
/// client↔serveur en process. Les réponses register/push/pull transitent par le JSON canonique réel
/// (mêmes options que le serveur) — ce qui valide au passage les DTOs client contre le format du fil §8.
/// </summary>
public sealed class FauxClientApi(ServiceApi service) : IClientApi
{
    /// <summary>Simule une coupure réseau : le serveur a traité le push, mais la réponse se perd.</summary>
    public int CoupuresAvantReponsePush { get; set; }

    public Task<Guid> EnregistrerAppareil(string nom, string plateforme, CancellationToken jeton = default)
    {
        var reponse = service.EnregistrerAppareil(new DemandeEnregistrementAppareil(nom, plateforme));
        return Task.FromResult(Fil<ReponseEnregistrementAppareil, ReponseEnregistrementClient>(reponse).AppareilId);
    }

    public Task<ReponsePushClient> Pousser(LotPush lot, CancellationToken jeton = default)
    {
        var reponse = service.Pousser(lot); // le serveur traite (idempotent, atomique) AVANT la coupure simulée
        if (CoupuresAvantReponsePush > 0)
        {
            CoupuresAvantReponsePush--;
            throw new HttpRequestException("coupure réseau simulée après traitement serveur");
        }
        return Task.FromResult(Fil<ReponsePushDto, ReponsePushClient>(reponse));
    }

    public Task<ReponsePullClient> Tirer(long depuis, int limite, CancellationToken jeton = default)
        => Task.FromResult(Fil<ReponsePullDto, ReponsePullClient>(service.Tirer(depuis, limite)));

    public Task<ReponsePurgeClient> Purger(LotPurge lot, CancellationToken jeton = default)
    {
        var r = service.Purger(lot);
        return Task.FromResult(new ReponsePurgeClient(
            r.Resultats.Select(x => new ResultatPurgeClient(x.ChangeId, x.Statut, x.ServerSeq, x.Rejoue, x.Motif)).ToList(),
            r.PurgesInduites.Select(p => new ChangementInduitClient(p.ChangeId, p.Entite, p.EntiteId, p.ServerSeq)).ToList()));
    }

    /// <summary>Sérialise la réponse serveur en JSON canonique puis la relit en DTO client (vrai format du fil).</summary>
    private static TCible Fil<TSource, TCible>(TSource source)
        => SerialisationCanonique.Deserialiser<TCible>(SerialisationCanonique.Serialiser(source));
}
