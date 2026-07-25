using DeuxiemeCerveau.App.Donnees;
using DeuxiemeCerveau.App.Fichiers;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Temps;
using DeuxiemeCerveau.Windows.Configuration;
using DeuxiemeCerveau.Windows.Services;
using Microsoft.Extensions.Configuration;

namespace DeuxiemeCerveau.Windows;

/// <summary>
/// Assemble le graphe applicatif au démarrage. Tout est SINGLETON : <c>BaseLocale</c> détient une
/// <c>SqliteConnection</c> unique partagée par tous les services, et <see cref="AccesDonnees"/>
/// sérialise les accès (l'interface et la synchro de fond ne doivent jamais s'entrelacer).
/// <para>
/// La coquille ne fait qu'assembler : aucune logique métier ici (garde-fou-architecture, règle 2).
/// </para>
/// </summary>
public sealed class Composition : IDisposable
{
    private readonly BaseLocale _baseLocale;
    private readonly HttpClient _httpApi;
    private readonly HttpClient _httpBlob;

    public OptionsApp Options { get; }
    public AccesDonnees Acces { get; }
    public IFournisseurJeton Jetons { get; }

    /// <summary>Exposé pour le seul état de synchro de l'interface (taille de l'outbox, curseur).</summary>
    public DepotLocal Depot { get; }

    public ServiceSaisie Saisie { get; }
    public ServiceLecture Lecture { get; }
    public ServiceCalendrier Calendrier { get; }
    public ServiceAujourdhui Aujourdhui { get; }
    public ServiceDemarrage Demarrage { get; }
    public ServiceConfirmation Confirmation { get; }
    public ServicePurge Purge { get; }
    public ServicePiecesJointes PiecesJointes { get; }
    public ServiceExport Export { get; }
    public ServiceImport Import { get; }
    public MoteurSynchro Synchro { get; }
    public IClientApi Api { get; }

    private Composition(
        OptionsApp options, BaseLocale baseLocale, IFournisseurJeton jetons,
        HttpClient httpApi, HttpClient httpBlob)
    {
        Options = options;
        _baseLocale = baseLocale;
        _httpApi = httpApi;
        _httpBlob = httpBlob;
        Jetons = jetons;
        Acces = new AccesDonnees();

        var depot = baseLocale.Depot;
        Depot = depot;
        var identite = new IdentiteAppareil(depot);
        var horloge = new HorlogeSysteme();
        var cache = new StockageFichiersDisque(DossierCache);

        Api = new ClientApiHttp(httpApi);
        Saisie = new ServiceSaisie(depot, identite, horloge);
        Lecture = new ServiceLecture(depot);
        Calendrier = new ServiceCalendrier(depot);
        Aujourdhui = new ServiceAujourdhui(depot, Calendrier);
        Demarrage = new ServiceDemarrage(depot, Saisie);
        Confirmation = new ServiceConfirmation(depot, Saisie);
        Purge = new ServicePurge(depot, new FilePurges(depot));
        PiecesJointes = new ServicePiecesJointes(depot, Saisie, cache, Api, new TransfertBlobHttp(httpBlob));
        Export = new ServiceExport(depot, horloge, cache);
        Import = new ServiceImport(depot, cache);
        Synchro = new MoteurSynchro(depot, identite, Api);
    }

    private static string DossierApp => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeuxiemeCerveau");

    private static string DossierCache => Path.Combine(DossierApp, "pieces");

    /// <summary>Chemin de la base locale. Jamais codé en dur ailleurs.</summary>
    public static string CheminBase => Path.Combine(DossierApp, "local.db");

    /// <summary>Vrai si l'API est configurée ET l'utilisateur connecté : la synchro peut tourner.</summary>
    public bool SynchroPossible => Options.Api.EstConfiguree && Jetons.Configure;

    public static async Task<Composition> Creer(Func<IntPtr> fenetreParente)
    {
        Directory.CreateDirectory(DossierApp);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.local.json", optional: true)
            .Build();

        var options = configuration.Get<OptionsApp>() ?? new OptionsApp();

        // Les migrations du cœur (dialecte Sqlite) sont appliquées DANS le constructeur —
        // jamais de schéma transcrit à la main (D-008, règle 18).
        var baseLocale = new BaseLocale($"Data Source={CheminBase}");

        var jetons = await FournisseurJetonMsal.Creer(options.Entra, fenetreParente).ConfigureAwait(false);

        // Deux clients HTTP distincts, délibérément :
        //  - l'API porte le Bearer Entra ;
        //  - le blob est adressé par URL SAS, qui porte DÉJÀ son autorisation. Y ajouter le Bearer
        //    serait une fuite de jeton vers le stockage.
        var httpApi = new HttpClient(new ManipulateurJeton(jetons) { InnerHandler = new ManipulateurReessai { InnerHandler = new HttpClientHandler() } })
        {
            Timeout = TimeSpan.FromSeconds(100),
        };
        if (options.Api.EstConfiguree) httpApi.BaseAddress = options.Api.BaseUri();

        var httpBlob = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        return new Composition(options, baseLocale, jetons, httpApi, httpBlob);
    }

    public void Dispose()
    {
        _httpApi.Dispose();
        _httpBlob.Dispose();
        Acces.Dispose();
        _baseLocale.Dispose();
    }
}
