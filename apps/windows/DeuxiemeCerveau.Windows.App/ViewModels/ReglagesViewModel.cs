using System.Windows.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;
using DeuxiemeCerveau.Windows.Core.Export;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class ReglagesViewModel : ViewModelBase
{
    private readonly IDepotLocal _depot;

    private string _apiUrl = "https://func-dc-dev-e3kdxc.azurewebsites.net";
    public string ApiUrl
    {
        get => _apiUrl;
        set => SetProperty(ref _apiUrl, value);
    }

    private double _nouveauSoldeReferenceEuros = 1500;
    public double NouveauSoldeReferenceEuros
    {
        get => _nouveauSoldeReferenceEuros;
        set => SetProperty(ref _nouveauSoldeReferenceEuros, value);
    }

    private string _statutSynchro = "Synchro active (en tâche de fond)";
    public string StatutSynchro
    {
        get => _statutSynchro;
        set => SetProperty(ref _statutSynchro, value);
    }

    public ICommand RecalerSoldeCommand { get; }
    public ICommand ExporterZipCommand { get; }

    public ReglagesViewModel(IDepotLocal depot)
    {
        _depot = depot;
        RecalerSoldeCommand = new RelayCommand(RecalerSolde);
        ExporterZipCommand = new RelayCommand(ExporterZip);
        Recharger();
    }

    public void Recharger()
    {
        var reg = _depot.ObtenirSoldeReference();
        if (reg is not null)
        {
            NouveauSoldeReferenceEuros = reg.SoldeReferenceCentimes / 100.0;
        }
    }

    private void RecalerSolde()
    {
        var reg = new ReglageSolde
        {
            Id = ReglageSolde.IdSoldeReference,
            SoldeReferenceCentimes = (long)Math.Round(NouveauSoldeReferenceEuros * 100),
            SoldeReferenceDate = DateOnly.FromDateTime(DateTime.Today),
            DateModification = DateTimeOffset.UtcNow
        };

        _depot.RecalerSoldeReference(reg, Guid.NewGuid());
        StatutSynchro = "Solde de référence recalé avec succès !";
    }

    private void ExporterZip()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var zipPath = System.IO.Path.Combine(desktop, $"DeuxiemeCerveau_Export_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            ExportateurLocal.ExporterZip(_depot, zipPath);
            StatutSynchro = $"Export ZIP généré sur le Bureau : {System.IO.Path.GetFileName(zipPath)}";
        }
        catch (Exception ex)
        {
            StatutSynchro = $"Erreur d'export : {ex.Message}";
        }
    }
}
