using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Synchro;

/// <summary>
/// Vue CLIENT de l'API §8 : ce dont le moteur de synchro a besoin du serveur. L'implémentation HTTP
/// (<see cref="ClientApiHttp"/>) parle à l'API Azure ; les tests utilisent un faux adossé au vrai
/// <c>ServiceApi</c> en process. L'adresse de l'API et le jeton d'auth sont portés par le HttpClient
/// (config §2, jamais codés en dur — règle 4).
/// </summary>
public interface IClientApi
{
    Task<Guid> EnregistrerAppareil(string nom, string plateforme, CancellationToken jeton = default);

    Task<ReponsePushClient> Pousser(LotPush lot, CancellationToken jeton = default);

    Task<ReponsePullClient> Tirer(long depuis, int limite, CancellationToken jeton = default);

    Task<ReponsePurgeClient> Purger(LotPurge lot, CancellationToken jeton = default);
}
