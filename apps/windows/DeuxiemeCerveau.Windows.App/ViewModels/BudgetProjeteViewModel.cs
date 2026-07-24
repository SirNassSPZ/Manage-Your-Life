using System.Collections.ObjectModel;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class MoisProjectionModel : ViewModelBase
{
    public string NomMois { get; }
    public long SoldeOuvertureCentimes { get; }
    public long TotalEntreesCentimes { get; }
    public long TotalSortiesCentimes { get; }
    public long SoldeClotureCentimes { get; }
    public bool EstDeficitaire => SoldeClotureCentimes < 0;

    public string SoldeOuvertureFormatted => $"{SoldeOuvertureCentimes / 100.0:N2} €";
    public string TotalEntreesFormatted => $"+{TotalEntreesCentimes / 100.0:N2} €";
    public string TotalSortiesFormatted => $"-{TotalSortiesCentimes / 100.0:N2} €";
    public string SoldeClotureFormatted => $"{SoldeClotureCentimes / 100.0:N2} €";

    public MoisProjectionModel(string nomMois, long soldeOuverture, long totalEntrees, long totalSorties, long soldeCloture)
    {
        NomMois = nomMois;
        SoldeOuvertureCentimes = soldeOuverture;
        TotalEntreesCentimes = totalEntrees;
        TotalSortiesCentimes = totalSorties;
        SoldeClotureCentimes = soldeCloture;
    }
}

public sealed class BudgetProjeteViewModel : ViewModelBase
{
    private readonly IDepotLocal _depot;

    public ObservableCollection<MoisProjectionModel> Projections { get; } = new();

    public BudgetProjeteViewModel(IDepotLocal depot)
    {
        _depot = depot;
        Recharger();
    }

    public void Recharger()
    {
        Projections.Clear();

        var soldeRef = _depot.ObtenirSoldeReference();
        long soldeCourant = soldeRef?.SoldeReferenceCentimes ?? 0;

        var tousElements = _depot.ListerElements()
            .Where(e => e.Type is TypeElement.Facture or TypeElement.Paiement or TypeElement.Revenu)
            .Where(e => !e.Supprime && e.Statut != StatutElement.Annule)
            .ToList();

        var aujourdhui = DateTime.Today;

        for (int i = 0; i < 12; i++)
        {
            var moisCible = aujourdhui.AddMonths(i);
            string nomMois = moisCible.ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")).ToUpperFirst();

            long entrees = tousElements
                .Where(e => e.Sens == Sens.Entree && (e.DateCreation.Month == moisCible.Month || i == 0))
                .Sum(e => e.MontantCentimes ?? 0);

            long sorties = tousElements
                .Where(e => e.Sens == Sens.Sortie && (e.DateCreation.Month == moisCible.Month || i == 0))
                .Sum(e => e.MontantCentimes ?? 0);

            long soldeCloture = soldeCourant + entrees - sorties;

            Projections.Add(new MoisProjectionModel(nomMois, soldeCourant, entrees, sorties, soldeCloture));

            // Continuous cascade to next month
            soldeCourant = soldeCloture;
        }
    }
}
