using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.Core.Export;

/// <summary>
/// DTO d'exportation complète (§5.7) : format ouvert et lisible, corbeille incluse.
/// Exécuté 100% côté client depuis la base locale SQLite, sans réseau.
/// </summary>
public sealed class DonneesExportDto
{
    public DateTimeOffset DateExport { get; set; } = DateTimeOffset.UtcNow;
    public string VersionSpec { get; set; } = "v3.1";
    public ReglageSolde? SoldeReference { get; set; }
    public List<Element> Elements { get; set; } = new();
    public List<Categorie> Categories { get; set; } = new();
    public List<Projet> Projets { get; set; } = new();
    public List<Budget> Budgets { get; set; } = new();
}

public static class ExportateurLocal
{
    /// <summary>
    /// Exécute l'exportation complète (§5.7) et génère l'archive ZIP sur le chemin cible.
    /// Contient <c>donnees.json</c> + le dossier <c>pieces_jointes/</c>.
    /// </summary>
    public static void ExporterZip(IDepotLocal depot, string cheminFichierZipTarget, string? dossierCachePiecesJointes = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dc_export_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var donnees = new DonneesExportDto
            {
                SoldeReference = depot.ObtenirSoldeReference(),
                Elements = depot.ListerElements(inclureSupprimes: true).ToList(),
                Categories = depot.ListerCategories(inclureSupprimes: true).ToList(),
                Projets = depot.ListerProjets(inclureSupprimes: true).ToList(),
                Budgets = depot.ListerBudgets(inclureSupprimes: true).ToList()
            };

            var optionsJson = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var jsonPath = Path.Combine(tempDir, "donnees.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(donnees, optionsJson));

            var pjDirTarget = Path.Combine(tempDir, "pieces_jointes");
            Directory.CreateDirectory(pjDirTarget);

            if (!string.IsNullOrWhiteSpace(dossierCachePiecesJointes) && Directory.Exists(dossierCachePiecesJointes))
            {
                foreach (var fichier in Directory.GetFiles(dossierCachePiecesJointes))
                {
                    File.Copy(fichier, Path.Combine(pjDirTarget, Path.GetFileName(fichier)), overwrite: true);
                }
            }

            if (File.Exists(cheminFichierZipTarget))
                File.Delete(cheminFichierZipTarget);

            ZipFile.CreateFromDirectory(tempDir, cheminFichierZipTarget, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

public static class ImportateurLocal
{
    /// <summary>
    /// Réimporte une archive ZIP (§5.7) dans une installation vierge.
    /// </summary>
    public static DonneesExportDto ImporterZip(IDepotLocal depot, string cheminFichierZip)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dc_import_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            ZipFile.ExtractToDirectory(cheminFichierZip, tempDir);
            var jsonPath = Path.Combine(tempDir, "donnees.json");
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("L'archive d'export ne contient pas donnees.json.");

            var json = File.ReadAllText(jsonPath);
            var donnees = JsonSerializer.Deserialize<DonneesExportDto>(json)
                ?? throw new InvalidOperationException("Format JSON invalide dans l'archive.");

            // Importer les entités dans le magasin local
            if (donnees.SoldeReference is not null)
                depot.RecalerSoldeReference(donnees.SoldeReference, Guid.NewGuid());

            foreach (var cat in donnees.Categories)
                depot.EnregistrerCategorie(cat);

            foreach (var prj in donnees.Projets)
                depot.EnregistrerProjet(prj);

            foreach (var bdg in donnees.Budgets)
                depot.EnregistrerBudget(bdg);

            foreach (var elem in donnees.Elements)
                depot.EnregistrerElement(elem);

            return donnees;
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
