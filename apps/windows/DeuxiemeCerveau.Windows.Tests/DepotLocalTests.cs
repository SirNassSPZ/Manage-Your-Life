using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;
using Xunit;

namespace DeuxiemeCerveau.Windows.Tests;

public sealed class DepotLocalTests : IDisposable
{
    private readonly string _fichDb;
    private readonly DepotLocalSqlite _depot;

    public DepotLocalTests()
    {
        _fichDb = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid()}.db");
        _depot = new DepotLocalSqlite($"Data Source={_fichDb}");
    }

    [Fact]
    public void EcritureLocaleImmediat_Et_GenereOutbox()
    {
        var elem = new Element
        {
            Titre = "Facture d'électricité (test local)",
            Type = TypeElement.Facture,
            MontantCentimes = 12000,
            Devise = "EUR",
            Sens = Sens.Sortie,
            Statut = StatutElement.AVenir
        };

        _depot.EnregistrerElement(elem);

        var lu = _depot.ObtenirElement(elem.Id);
        Assert.NotNull(lu);
        Assert.Equal("Facture d'électricité (test local)", lu.Titre);
        Assert.Equal(12000, lu.MontantCentimes);

        var outbox = _depot.ObtenirOutbox();
        Assert.Single(outbox);
        Assert.Equal(elem.Id, outbox[0].EntiteId);
        Assert.Equal(1, outbox[0].Version);
    }

    [Fact]
    public void SoftDelete_NeDetruitPas_MaisMarqueSupprime()
    {
        var elem = new Element
        {
            Titre = "Rendez-vous médecin (test delete)",
            Type = TypeElement.Rendezvous,
            Statut = StatutElement.Planifie
        };

        _depot.EnregistrerElement(elem);
        _depot.SupprimerElement(elem.Id);

        var sansSuppr = _depot.ListerElements(inclureSupprimes: false);
        Assert.Empty(sansSuppr);

        var avecSuppr = _depot.ListerElements(inclureSupprimes: true);
        Assert.Single(avecSuppr);
        Assert.True(avecSuppr[0].Supprime);
        Assert.NotNull(avecSuppr[0].DateSuppression);
    }

    public void Dispose()
    {
        _depot.Dispose();
        if (File.Exists(_fichDb))
        {
            try { File.Delete(_fichDb); } catch { }
        }
    }
}
