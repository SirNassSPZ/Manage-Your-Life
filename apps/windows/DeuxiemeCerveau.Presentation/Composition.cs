using DeuxiemeCerveau.App.Donnees;
using DeuxiemeCerveau.App.Fichiers;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.Presentation;

/// <summary>
/// Assemble le graphe applicatif au démarrage. Tout est SINGLETON : <c>BaseLocale</c> détient une
/// <c>SqliteConnection</c> unique partagée par tous les services, et <see cref="AccesDonnees"/>
/// sérialise les accès (l'interface et la synchro de fond ne doivent jamais s'entrelacer).
/// <para>
/// Assemble seulement : aucune logique métier ici (garde-fou-architecture, règle 2).
/// </para>
/// <para>
/// La configuration et le fournisseur de jetons sont <b>fournis</b>, jamais construits ici (D-022) :
/// lire un fichier sur le disque et parler à MSAL sont des affaires d'hôte. C'est aussi ce qui rend
/// le graphe montable dans un test — un <see cref="FournisseurJetonAbsent"/>, une base temporaire,
/// et tout le reste est le vrai code de production.
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

    /// <summary>
    /// Dossier de données de ce poste. Ce qui n'a pas sa place dans la base synchronisée y vit
    /// aussi — le journal des rappels, par exemple (D-023).
    /// </summary>
    public string Dossier { get; }

    /// <summary>Exposé pour le seul état de synchro de l'interface (taille de l'outbox, curseur).</summary>
    public DepotLocal Depot { get; }

    public ServiceSaisie Saisie { get; }
    public ServiceLecture Lecture { get; }
    public ServiceCalendrier Calendrier { get; }
    public ServiceAujourdhui Aujourdhui { get; }
    public ServiceDemarrage Demarrage { get; }
    public ServiceConfirmation Confirmation { get; }
    public ServiceRecalage Recalage { get; }
    public ServicePurge Purge { get; }
    public ServicePiecesJointes PiecesJointes { get; }
    public ServiceExport Export { get; }
    public ServiceImport Import { get; }
    public MoteurSynchro Synchro { get; }
    public IClientApi Api { get; }

    private Composition(
        OptionsApp options, BaseLocale baseLocale, IFournisseurJeton jetons,
        HttpClient httpApi, HttpClient httpBlob, string dossier, string dossierCache)
    {
        Options = options;
        Dossier = dossier;
        _baseLocale = baseLocale;
        _httpApi = httpApi;
        _httpBlob = httpBlob;
        Jetons = jetons;
        Acces = new AccesDonnees();

        var depot = baseLocale.Depot;
        Depot = depot;
        var identite = new IdentiteAppareil(depot);
        var horloge = new HorlogeSysteme();
        var cache = new StockageFichiersDisque(dossierCache);

        Api = new ClientApiHttp(httpApi);
        Saisie = new ServiceSaisie(depot, identite, horloge);
        Lecture = new ServiceLecture(depot);
        Calendrier = new ServiceCalendrier(depot);
        Aujourdhui = new ServiceAujourdhui(depot, Calendrier);
        Demarrage = new ServiceDemarrage(depot, Saisie);
        Confirmation = new ServiceConfirmation(depot, Saisie);
        Recalage = new ServiceRecalage(Aujourdhui, Demarrage);
        Purge = new ServicePurge(depot, new FilePurges(depot));
        PiecesJointes = new ServicePiecesJointes(depot, Saisie, cache, Api, new TransfertBlobHttp(httpBlob), Acces);
        Export = new ServiceExport(depot, horloge, cache);
        Import = new ServiceImport(depot, cache);
        // Acces sert de porte : le moteur la referme autour de chaque touche à la base et la laisse
        // ouverte pendant le réseau. Sans elle, un réveil serverless gèlerait l'interface (filet 1).
        Synchro = new MoteurSynchro(depot, identite, Api, Acces);
    }

    /// <summary>Dossier de données par défaut de l'utilisateur courant.</summary>
    public static string DossierParDefaut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeuxiemeCerveau");

    /// <summary>Vrai si l'API est configurée ET l'utilisateur connecté : la synchro peut tourner.</summary>
    public bool SynchroPossible => Options.Api.EstConfiguree && Jetons.Configure;

    /// <summary>
    /// Monte le graphe dans <paramref name="dossierDonnees"/> (défaut : <see cref="DossierParDefaut"/>).
    /// Les migrations du cœur (dialecte Sqlite) s'appliquent dans le constructeur de
    /// <c>BaseLocale</c> — jamais de schéma transcrit à la main (D-008, règle 18).
    /// </summary>
    /// <param name="manipulateurApi">
    /// Chaîne de gestionnaires HTTP de l'API, fournie par l'hôte : injection du Bearer et réessai au
    /// réveil serverless. Absente, un gestionnaire nu suffit — un test n'a pas besoin de cette pile.
    /// </param>
    public static Composition Creer(
        OptionsApp options,
        IFournisseurJeton jetons,
        string? dossierDonnees = null,
        Func<IFournisseurJeton, HttpMessageHandler>? manipulateurApi = null)
    {
        var dossier = dossierDonnees ?? DossierParDefaut;
        Directory.CreateDirectory(dossier);

        var baseLocale = new BaseLocale($"Data Source={Path.Combine(dossier, "local.db")}");

        // Deux clients HTTP distincts, délibérément :
        //  - l'API porte le Bearer Entra ;
        //  - le blob est adressé par URL SAS, qui porte DÉJÀ son autorisation. Y ajouter le Bearer
        //    serait une fuite de jeton vers le stockage.
        // 180 s et non 100 : le démarrage à froid mesuré sur l'API déployée est de ~61 s
        // (Functions Consommation + reprise du SQL serverless, §10.1), et les tentatives de
        // réessai s'ajoutent par-dessus. L'interface n'attend jamais le serveur.
        var manipulateur = manipulateurApi?.Invoke(jetons) ?? new HttpClientHandler();
        var httpApi = new HttpClient(manipulateur) { Timeout = TimeSpan.FromSeconds(180) };
        if (options.Api.EstConfiguree) httpApi.BaseAddress = options.Api.BaseUri();

        var httpBlob = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        return new Composition(
            options, baseLocale, jetons, httpApi, httpBlob, dossier, Path.Combine(dossier, "pieces"));
    }

    public void Dispose()
    {
        _httpApi.Dispose();
        _httpBlob.Dispose();
        Acces.Dispose();
        _baseLocale.Dispose();
    }
}
