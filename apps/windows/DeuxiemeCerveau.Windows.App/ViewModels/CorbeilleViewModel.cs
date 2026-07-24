using System.Collections.ObjectModel;
using System.Windows.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class CorbeilleViewModel : ViewModelBase
{
    private readonly IDepotLocal _depot;

    public ObservableCollection<Element> ElementsSupprimes { get; } = new();

    public ICommand RestaurerCommand { get; }
    public ICommand PurgerCommand { get; }

    public CorbeilleViewModel(IDepotLocal depot)
    {
        _depot = depot;
        RestaurerCommand = new RelayCommand(RestaurerElement);
        PurgerCommand = new RelayCommand(PurgerElement);
        Recharger();
    }

    public void Recharger()
    {
        ElementsSupprimes.Clear();
        var supprimes = _depot.ListerElements(inclureSupprimes: true)
            .Where(e => e.Supprime)
            .OrderByDescending(e => e.DateSuppression ?? e.DateModification);

        foreach (var elem in supprimes)
        {
            ElementsSupprimes.Add(elem);
        }
    }

    private void RestaurerElement()
    {
        // Restaure le premier élément sélectionné
        if (ElementsSupprimes.FirstOrDefault() is { } elem)
        {
            _depot.RestaurerElement(elem.Id);
            Recharger();
        }
    }

    private void PurgerElement()
    {
        // Purge définitivement le premier élément (§5.6)
        if (ElementsSupprimes.FirstOrDefault() is { } elem)
        {
            _depot.PurgerLocalement(EntiteSynchro.Element, elem.Id);
            Recharger();
        }
    }
}
