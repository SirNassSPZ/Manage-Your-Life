using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.App.Donnees;

/// <summary>
/// Export local complet (§5.7, NON NÉGOCIABLE) : produit une archive ZIP « { donnees.json +
/// pieces_jointes/ } » depuis la SEULE base locale, SANS réseau. Corbeille comprise. Format ouvert,
/// lisible sans l'app. C'est le point décisif : l'export doit fonctionner au moment précis où le
/// serveur est inaccessible — aucune dépendance serveur autorisée.
/// </summary>
public sealed class ServiceExport(DepotLocal depot, IHorloge horloge)
{
    public const string FichierDonnees = "donnees.json";
    public const string DossierPieces = "pieces_jointes/";
    public const int VersionFormat = 1;

    /// <summary>Écrit l'archive ZIP dans <paramref name="sortie"/> (fourni par la plateforme : fichier choisi par l'utilisateur).</summary>
    public void Exporter(Stream sortie)
    {
        using var zip = new ZipArchive(sortie, ZipArchiveMode.Create, leaveOpen: true);

        var entree = zip.CreateEntry(FichierDonnees, CompressionLevel.Optimal);
        using (var flux = entree.Open())
        {
            var octets = Encoding.UTF8.GetBytes(ConstruireDonnees());
            flux.Write(octets, 0, octets.Length);
        }

        // Dossier des pièces jointes (§7) : alimenté par le cache local des fichiers. Le cache n'est pas
        // encore constitué (feature client à venir) — une pièce absente du cache est, par la spec,
        // signalée manquante ; ici le dossier est simplement vide.
        zip.CreateEntry(DossierPieces);
    }

    private string ConstruireDonnees()
    {
        List<JsonElement> Lire(EntiteSynchro type)
            => depot.Enumerer(type).Select(e => JsonDocument.Parse(e.PayloadCanonique).RootElement.Clone()).ToList();

        var reglageEtat = depot.Obtenir(EntiteSynchro.Reglage, ReglageSolde.IdSoldeReference);
        JsonElement? reglage = reglageEtat is null
            ? null
            : JsonDocument.Parse(reglageEtat.PayloadCanonique).RootElement.Clone();

        var archive = new ArchiveDonnees(
            VersionFormat, horloge.MaintenantUtc,
            Lire(EntiteSynchro.Element), Lire(EntiteSynchro.Categorie), Lire(EntiteSynchro.Projet),
            Lire(EntiteSynchro.Budget), Lire(EntiteSynchro.PieceJointe), reglage);
        return SerialisationCanonique.Serialiser(archive);
    }
}
