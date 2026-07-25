namespace DeuxiemeCerveau.Windows.Services;

/// <summary>
/// Fournit le jeton Bearer Entra ID pour l'API (§8).
/// <para>
/// Contrat non négociable : <see cref="ObtenirSilencieux"/> ne déclenche JAMAIS d'interactif —
/// sinon un navigateur s'ouvrirait tout seul pendant une synchro de fond. La connexion est un
/// geste explicite de l'utilisateur, et l'app reste pleinement utilisable hors ligne (filet 1).
/// </para>
/// </summary>
public interface IFournisseurJeton
{
    /// <summary>Vrai si une inscription Entra est configurée (sinon : mode hors ligne).</summary>
    bool Configure { get; }

    /// <summary>Jeton d'accès, ou null si non connecté. Ne déclenche jamais d'interactif.</summary>
    Task<string?> ObtenirSilencieux(CancellationToken jeton = default);

    /// <summary>Connexion explicite, déclenchée par l'utilisateur.</summary>
    Task<bool> Connecter(CancellationToken jeton = default);

    /// <summary>Déconnexion : purge le compte du cache de jetons.</summary>
    Task Deconnecter(CancellationToken jeton = default);

    /// <summary>Nom du compte connecté, ou null.</summary>
    Task<string?> CompteConnecte(CancellationToken jeton = default);
}

/// <summary>Utilisé quand aucune inscription Entra n'est configurée : l'app fonctionne hors ligne.</summary>
public sealed class FournisseurJetonAbsent : IFournisseurJeton
{
    public bool Configure => false;
    public Task<string?> ObtenirSilencieux(CancellationToken jeton = default) => Task.FromResult<string?>(null);
    public Task<bool> Connecter(CancellationToken jeton = default) => Task.FromResult(false);
    public Task Deconnecter(CancellationToken jeton = default) => Task.CompletedTask;
    public Task<string?> CompteConnecte(CancellationToken jeton = default) => Task.FromResult<string?>(null);
}
