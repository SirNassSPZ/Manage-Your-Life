using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DeuxiemeCerveau.App.Fichiers;
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
public sealed class ServiceExport(DepotLocal depot, IHorloge horloge, IStockageFichiersLocal? cache = null)
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

        // Pièces jointes (§7) : le dossier reçoit les fichiers présents dans le cache local. Une pièce
        // absente du cache est simplement omise (elle sera signalée manquante par sa présence dans
        // donnees.json sans fichier, §5.7). Nom d'entrée = « {id}__{nom} » : l'identifiant, robuste, en
        // tête, puis le nom d'origine pour rester lisible.
        zip.CreateEntry(DossierPieces);
        if (cache is not null)
            foreach (var etat in depot.Enumerer(EntiteSynchro.PieceJointe))
            {
                var piece = SerialisationCanonique.Deserialiser<PieceJointe>(etat.PayloadCanonique);
                if (piece.Supprime)
                    continue;
                if (cache.Lire(piece.Id) is not { } contenu)
                    continue;
                var nom = $"{DossierPieces}{piece.Id:D}__{NomSur(piece.NomFichier)}";
                var e = zip.CreateEntry(nom, CompressionLevel.Optimal);
                using var f = e.Open();
                f.Write(contenu, 0, contenu.Length);
            }
    }

    private static string NomSur(string nom)
    {
        var invalides = Path.GetInvalidFileNameChars();
        var propre = new string(nom.Select(c => invalides.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(propre) ? "fichier" : propre;
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
