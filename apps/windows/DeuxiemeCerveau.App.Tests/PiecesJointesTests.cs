using DeuxiemeCerveau.Api.Persistence;
using DeuxiemeCerveau.Api.Services;
using DeuxiemeCerveau.App.Fichiers;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Pièces jointes côté app (§7) — Étape 4e-bis. Local-first (cache immédiat), envoi en tâche de fond
/// via URL SAS (téléversement direct vers un faux Blob), lecture avec mise en cache, limite 25 Mo,
/// aller-retour entre deux appareils (l'un joint et envoie, l'autre télécharge).
/// </summary>
public sealed class PiecesJointesTests : IDisposable
{
    private readonly HorlogeFixe _horloge = new(FabriqueLocale.T0);
    private readonly ServiceApi _service;
    private readonly FauxClientApi _api;
    private readonly TransfertBlobMemoire _blob = new();
    private readonly List<BaseLocale> _bases = [];

    public PiecesJointesTests()
    {
        _service = new ServiceApi(
            new MagasinSynchroMemoire(), new MagasinAppareilsMemoire(_horloge), _horloge, new StockagePiecesMemoire());
        _api = new FauxClientApi(_service);
    }

    public void Dispose()
    {
        foreach (var b in _bases)
            b.Dispose();
    }

    private (BaseLocale b, StockageFichiersMemoire cache, MoteurSynchro moteur, ServicePiecesJointes pj) Appareil()
    {
        var b = FabriqueLocale.BaseMemoire();
        _bases.Add(b);
        var id = new IdentiteAppareil(b.Depot);
        var cache = new StockageFichiersMemoire();
        var saisie = new ServiceSaisie(b.Depot, id, _horloge);
        var moteur = new MoteurSynchro(b.Depot, id, _api);
        var pj = new ServicePiecesJointes(b.Depot, saisie, cache, _api, _blob);
        return (b, cache, moteur, pj);
    }

    private static byte[] Fichier(int taille = 2048)
    {
        var octets = new byte[taille];
        new Random(42).NextBytes(octets);
        return octets;
    }

    private static PieceJointe Piece(BaseLocale b)
        => SerialisationCanonique.Deserialiser<PieceJointe>(
            b.Depot.Enumerer(EntiteSynchro.PieceJointe).Single().PayloadCanonique);

    [Fact]
    public void Joindre_met_en_cache_et_cree_des_metadonnees_non_confirmees()
    {
        var (b, cache, _, pj) = Appareil();
        var resultat = pj.Joindre(Guid.NewGuid(), "facture.pdf", Fichier());

        Assert.True(resultat.Reussi);
        var piece = Piece(b);
        Assert.False(piece.Confirme);                 // pas encore envoyée
        Assert.Equal("facture.pdf", piece.NomFichier);
        Assert.True(cache.Existe(piece.Id));          // en cache IMMÉDIATEMENT (§7, local-first)
        Assert.Single(b.Depot.Outbox());              // les métadonnées partiront à la synchro
    }

    [Fact]
    public void Fichier_trop_volumineux_refuse()
    {
        var (b, _, _, pj) = Appareil();
        var resultat = pj.Joindre(Guid.NewGuid(), "gros.bin", new byte[PieceJointe.TailleMaxOctets + 1]);

        Assert.False(resultat.Reussi);
        Assert.Empty(b.Depot.Enumerer(EntiteSynchro.PieceJointe)); // rien créé
    }

    [Fact]
    public async Task Envoyer_televerse_puis_marque_confirme()
    {
        var (b, _, _, pj) = Appareil();
        pj.Joindre(Guid.NewGuid(), "facture.pdf", Fichier());

        await pj.EnvoyerEnAttente();

        Assert.True(Piece(b).Confirme); // upload + confirmation réussis
    }

    [Fact]
    public async Task Ouvrir_depuis_le_cache_local_sans_reseau()
    {
        var (b, _, _, pj) = Appareil();
        var contenu = Fichier();
        pj.Joindre(Guid.NewGuid(), "note.txt", contenu);

        var relu = await pj.Ouvrir(Piece(b).Id);
        Assert.Equal(contenu, relu);
    }

    [Fact]
    public async Task Ouvrir_sur_un_autre_appareil_telecharge_et_met_en_cache()
    {
        // Appareil A : joint, envoie, synchronise (les métadonnées confirmées partent au serveur).
        var (ba, _, ma, pja) = Appareil();
        var contenu = Fichier(4096);
        pja.Joindre(Guid.NewGuid(), "recu.jpg", contenu);
        await pja.EnvoyerEnAttente();
        await ma.Synchroniser("A", "windows");
        var pieceId = Piece(ba).Id;

        // Appareil B : tire les métadonnées, n'a pas le fichier en cache, puis l'ouvre → téléchargement.
        var (_, cacheB, mb, pjb) = Appareil();
        await mb.Synchroniser("B", "windows");
        Assert.False(cacheB.Existe(pieceId));

        var relu = await pjb.Ouvrir(pieceId);
        Assert.Equal(contenu, relu);          // téléchargé depuis le Blob partagé
        Assert.True(cacheB.Existe(pieceId));  // mis en cache local après lecture (§7)
    }
}
