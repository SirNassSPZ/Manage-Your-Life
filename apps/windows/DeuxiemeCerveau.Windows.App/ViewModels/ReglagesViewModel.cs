using System.Windows.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;
using DeuxiemeCerveau.Windows.Core.Export;
using Microsoft.Win32;

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

    private double _nouveauSoldeReferenceEuros = 2450;
    public double NouveauSoldeReferenceEuros
    {
        get => _nouveauSoldeReferenceEuros;
        set => SetProperty(ref _nouveauSoldeReferenceEuros, value);
    }

    private bool _rappelExportMensuelActif = true;
    public bool RappelExportMensuelActif
    {
        get => _rappelExportMensuelActif;
        set => SetProperty(ref _rappelExportMensuelActif, value);
    }

    private string _statutSynchro = "Base SQLite locale active — Synchro HTTPS configurée";
    public string StatutSynchro
    {
        get => _statutSynchro;
        set => SetProperty(ref _statutSynchro, value);
    }

    public ICommand RecalerSoldeCommand { get; }
    public ICommand ExporterZipCommand { get; }
    public ICommand ImporterZipCommand { get; }

    public ReglagesViewModel(IDepotLocal depot)
    {
        _depot = depot;
        RecalerSoldeCommand = new RelayCommand(RecalerSolde);
        ExporterZipCommand = new RelayCommand(ExporterZip);
        ImporterZipCommand = new RelayCommand(ImporterZip);
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
        StatutSynchro = "Solde de référence recalé (§3.4) !";
    }

    private void ExporterZip()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var zipPath = System.IO.Path.Combine(desktop, $"DeuxiemeCerveau_Export_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            ExportateurLocal.ExporterZip(_depot, zipPath);
            StatutSynchro = $"Export ZIP local généré sans réseau (§5.7) sur le Bureau : {System.IO.Path.GetFileName(zipPath)}";
        }
        catch (Exception ex)
        {
            StatutSynchro = $"Erreur d'export : {ex.Message}";
        }
    }

    private void ImporterZip()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Archive d'export Deuxième Cerveau (*.zip)|*.zip",
                Title = "Sélectionner une archive d'exportation ZIP (§5.7)"
            };

            if (dialog.ShowDialog() == true)
            {
                var donnees = ImportateurLocal.ImporterZip(_depot, dialog.FileName);
                StatutSynchro = $"Import réussi ! {donnees.Elements.Count} éléments restaurés depuis {System.IO.Path.GetFileName(dialog.FileName)}.";
                Recharger();
            }
        }
        catch (Exception ex)
        {
            StatutSynchro = $"Erreur d'import : {ex.Message}";
        }
    }
}
