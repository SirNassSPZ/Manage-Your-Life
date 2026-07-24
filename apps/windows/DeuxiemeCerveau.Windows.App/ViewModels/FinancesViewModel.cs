using System.Collections.ObjectModel;
using System.Windows.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class FinancesViewModel : ViewModelBase
{
    private readonly IDepotLocal _depot;

    private long _soldeCentimes;
    public long SoldeCentimes
    {
        get => _soldeCentimes;
        set { SetProperty(ref _soldeCentimes, value); OnPropertyChanged(nameof(SoldeEurosFormatted)); }
    }

    public string SoldeEurosFormatted => $"{_soldeCentimes / 100.0:F2} €";

    private string _nouveauTitre = string.Empty;
    public string NouveauTitre
    {
        get => _nouveauTitre;
        set => SetProperty(ref _nouveauTitre, value);
    }

    private double _nouveauMontantEuros;
    public double NouveauMontantEuros
    {
        get => _nouveauMontantEuros;
        set => SetProperty(ref _nouveauMontantEuros, value);
    }

    private TypeElement _typeSelectionne = TypeElement.Facture;
    public TypeElement TypeSelectionne
    {
        get => _typeSelectionne;
        set => SetProperty(ref _typeSelectionne, value);
    }

    public ObservableCollection<Element> Transactions { get; } = new();

    public ICommand AjouterTransactionCommand { get; }

    public FinancesViewModel(IDepotLocal depot)
    {
        _depot = depot;
        AjouterTransactionCommand = new RelayCommand(AjouterTransaction);
        Recharger();
    }

    public void Recharger()
    {
        var soldeRef = _depot.ObtenirSoldeReference();
        SoldeCentimes = soldeRef?.SoldeReferenceCentimes ?? 0;

        Transactions.Clear();
        var elementsFinanciers = _depot.ListerElements()
            .Where(e => e.Type is TypeElement.Facture or TypeElement.Paiement or TypeElement.Revenu)
            .OrderByDescending(e => e.DateCreation);

        foreach (var elem in elementsFinanciers)
        {
            Transactions.Add(elem);
        }
    }

    private void AjouterTransaction()
    {
        if (string.IsNullOrWhiteSpace(NouveauTitre) || NouveauMontantEuros <= 0)
            return;

        var elem = new Element
        {
            Titre = NouveauTitre.Trim(),
            Type = TypeSelectionne,
            MontantCentimes = (long)Math.Round(NouveauMontantEuros * 100),
            Devise = "EUR",
            Sens = TypeSelectionne == TypeElement.Revenu ? Sens.Entree : Sens.Sortie,
            Statut = TypeSelectionne == TypeElement.Revenu ? StatutElement.Attendu : StatutElement.AVenir
        };

        _depot.EnregistrerElement(elem);

        NouveauTitre = string.Empty;
        NouveauMontantEuros = 0;
        Recharger();
    }
}
