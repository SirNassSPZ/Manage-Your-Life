using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;
using DeuxiemeCerveau.Windows.Core.Export;
using Xunit;

namespace DeuxiemeCerveau.Windows.Tests;

public sealed class ExportImportTests
{
    [Fact]
    public void ExportZip_HorsLigne_Et_ImportVierge()
    {
        var fDbSource = Path.Combine(Path.GetTempPath(), $"db_src_{Guid.NewGuid()}.db");
        var fDbCible = Path.Combine(Path.GetTempPath(), $"db_dst_{Guid.NewGuid()}.db");
        var fZip = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid()}.zip");

        try
        {
            using var depotSrc = new DepotLocalSqlite($"Data Source={fDbSource}");

            var elem1 = new Element { Titre = "Loyer", Type = TypeElement.Facture, MontantCentimes = 80000, Statut = StatutElement.AVenir };
            var elem2 = new Element { Titre = "Note supprimée", Type = TypeElement.Note, Statut = StatutElement.Active };
            depotSrc.EnregistrerElement(elem1);
            depotSrc.EnregistrerElement(elem2);
            depotSrc.SupprimerElement(elem2.Id); // Corbeille

            var reg = new ReglageSolde { SoldeReferenceCentimes = 150000, SoldeReferenceDate = new DateOnly(2026, 7, 1) };
            depotSrc.RecalerSoldeReference(reg, Guid.NewGuid());

            // Exportation ZIP (sans réseau)
            ExportateurLocal.ExporterZip(depotSrc, fZip);
            Assert.True(File.Exists(fZip));

            // Importation dans une base vierge
            using var depotDst = new DepotLocalSqlite($"Data Source={fDbCible}");
            var donnees = ImportateurLocal.ImporterZip(depotDst, fZip);

            Assert.Equal(2, donnees.Elements.Count);
            Assert.Equal(150000, donnees.SoldeReference?.SoldeReferenceCentimes);

            var elementsDistants = depotDst.ListerElements(inclureSupprimes: true);
            Assert.Equal(2, elementsDistants.Count);
            Assert.Contains(elementsDistants, e => e.Titre == "Loyer");
            Assert.Contains(elementsDistants, e => e.Titre == "Note supprimée" && e.Supprime);
        }
        finally
        {
            if (File.Exists(fDbSource)) try { File.Delete(fDbSource); } catch { }
            if (File.Exists(fDbCible)) try { File.Delete(fDbCible); } catch { }
            if (File.Exists(fZip)) try { File.Delete(fZip); } catch { }
        }
    }
}
