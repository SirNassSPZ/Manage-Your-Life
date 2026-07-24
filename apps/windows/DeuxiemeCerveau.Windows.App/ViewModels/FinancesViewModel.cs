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

    public string SoldeEurosFormatted => $"{_soldeCentimes / 100.0:N2} €";

    private long _totalEntreesCentimes;
    public long TotalEntreesCentimes
    {
        get => _totalEntreesCentimes;
        set { SetProperty(ref _totalEntreesCentimes, value); OnPropertyChanged(nameof(TotalEntreesFormatted)); }
    }
    public string TotalEntreesFormatted => $"+{_totalEntreesCentimes / 100.0:N2} €";

    private long _totalSortiesCentimes;
    public long TotalSortiesCentimes
    {
        get => _totalSortiesCentimes;
        set { SetProperty(ref _totalSortiesCentimes, value); OnPropertyChanged(nameof(TotalSortiesFormatted)); }
    }
    public string TotalSortiesFormatted => $"-{_totalSortiesCentimes / 100.0:N2} €";

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
        set
        {
            if (SetProperty(ref _typeSelectionne, value))
            {
                OnPropertyChanged(nameof(EstFacture));
                OnPropertyChanged(nameof(EstPaiement));
                OnPropertyChanged(nameof(EstRevenu));
            }
        }
    }

    public bool EstFacture => TypeSelectionne == TypeElement.Facture;
    public bool EstPaiement => TypeSelectionne == TypeElement.Paiement;
    public bool EstRevenu => TypeSelectionne == TypeElement.Revenu;

    private string _rechercheTexte = string.Empty;
    public string RechercheTexte
    {
        get => _rechercheTexte;
        set
        {
            if (SetProperty(ref _rechercheTexte, value))
                FiltrerTransactions();
        }
    }

    private List<Element> _toutesTransactions = new();
    public ObservableCollection<Element> Transactions { get; } = new();

    public ICommand AjouterTransactionCommand { get; }
    public ICommand ChoisirFactureCommand { get; }
    public ICommand ChoisirPaiementCommand { get; }
    public ICommand ChoisirRevenuCommand { get; }

    public FinancesViewModel(IDepotLocal depot)
    {
        _depot = depot;
        AjouterTransactionCommand = new RelayCommand(AjouterTransaction);
        ChoisirFactureCommand = new RelayCommand(() => TypeSelectionne = TypeElement.Facture);
        ChoisirPaiementCommand = new RelayCommand(() => TypeSelectionne = TypeElement.Paiement);
        ChoisirRevenuCommand = new RelayCommand(() => TypeSelectionne = TypeElement.Revenu);
        Recharger();
    }

    public void Recharger()
    {
        var soldeRef = _depot.ObtenirSoldeReference();
        SoldeCentimes = soldeRef?.SoldeReferenceCentimes ?? 0;

        _toutesTransactions = _depot.ListerElements()
            .Where(e => e.Type is TypeElement.Facture or TypeElement.Paiement or TypeElement.Revenu)
            .OrderByDescending(e => e.DateCreation)
            .ToList();

        TotalEntreesCentimes = _toutesTransactions.Where(e => e.Sens == Sens.Entree).Sum(e => e.MontantCentimes ?? 0);
        TotalSortiesCentimes = _toutesTransactions.Where(e => e.Sens == Sens.Sortie).Sum(e => e.MontantCentimes ?? 0);

        FiltrerTransactions();
    }

    private void FiltrerTransactions()
    {
        Transactions.Clear();
        var q = _toutesTransactions.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(RechercheTexte))
        {
            q = q.Where(e => e.Titre.Contains(RechercheTexte, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var elem in q)
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
