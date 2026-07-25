using DeuxiemeCerveau.Presentation;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace DeuxiemeCerveau.Windows.Services;

/// <summary>
/// Authentification Entra ID par MSAL.NET, en application NON EMPAQUETÉE.
/// <para>
/// Navigateur système + boucle locale (<c>http://localhost</c>) plutôt que le broker WAM : le
/// broker imposerait Microsoft.Identity.Client.NativeInterop (natif, sensible au RID) et une URI
/// de redirection supplémentaire dont le comportement en unpackaged est mal documenté.
/// </para>
/// </summary>
public sealed class FournisseurJetonMsal : IFournisseurJeton
{
    private readonly IPublicClientApplication _msal;
    private readonly string[] _portees;
    private readonly Func<IntPtr> _fenetreParente;
    private readonly SemaphoreSlim _porte = new(1, 1);

    private FournisseurJetonMsal(IPublicClientApplication msal, string[] portees, Func<IntPtr> fenetreParente)
    {
        _msal = msal;
        _portees = portees;
        _fenetreParente = fenetreParente;
    }

    public bool Configure => true;

    public static async Task<IFournisseurJeton> Creer(OptionsEntra options, Func<IntPtr> fenetreParente)
    {
        if (!options.EstConfiguree) return new FournisseurJetonAbsent();

        var msal = PublicClientApplicationBuilder
            .Create(options.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, options.TenantId)
            .WithRedirectUri("http://localhost")
            .WithClientName("Deuxieme Cerveau (Windows)")
            .Build();

        // Cache persistant chiffré par DPAPI : sans lui, reconnexion à chaque lancement.
        var dossier = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeuxiemeCerveau");
        Directory.CreateDirectory(dossier);
        var proprietes = new StorageCreationPropertiesBuilder("jetons.msalcache.bin", dossier).Build();
        (await MsalCacheHelper.CreateAsync(proprietes).ConfigureAwait(false)).RegisterCache(msal.UserTokenCache);

        return new FournisseurJetonMsal(msal, options.Portees, fenetreParente);
    }

    public async Task<string?> ObtenirSilencieux(CancellationToken jeton = default)
    {
        await _porte.WaitAsync(jeton).ConfigureAwait(false);
        try
        {
            var compte = (await _msal.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
            if (compte is null) return null;
            var resultat = await _msal.AcquireTokenSilent(_portees, compte).ExecuteAsync(jeton).ConfigureAwait(false);
            return resultat.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            // La synchro de fond abandonne silencieusement ; l'utilisateur se reconnectera.
            return null;
        }
        finally { _porte.Release(); }
    }

    public async Task<bool> Connecter(CancellationToken jeton = default)
    {
        await _porte.WaitAsync(jeton).ConfigureAwait(false);
        try
        {
            var resultat = await _msal.AcquireTokenInteractive(_portees)
                .WithParentActivityOrWindow(_fenetreParente())
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync(jeton)
                .ConfigureAwait(false);
            return !string.IsNullOrEmpty(resultat.AccessToken);
        }
        catch (MsalException)
        {
            return false;
        }
        finally { _porte.Release(); }
    }

    public async Task Deconnecter(CancellationToken jeton = default)
    {
        await _porte.WaitAsync(jeton).ConfigureAwait(false);
        try
        {
            foreach (var compte in await _msal.GetAccountsAsync().ConfigureAwait(false))
                await _msal.RemoveAsync(compte).ConfigureAwait(false);
        }
        finally { _porte.Release(); }
    }

    public async Task<string?> CompteConnecte(CancellationToken jeton = default)
    {
        var compte = (await _msal.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
        return compte?.Username;
    }
}
