using System.Collections.ObjectModel;
using System.Windows.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class CalendrierViewModel : ViewModelBase
{
    private readonly IDepotLocal _depot;

    public ObservableCollection<Element> Evenements { get; } = new();

    private string _nouveauRendezvousTitre = string.Empty;
    public string NouveauRendezvousTitre
    {
        get => _nouveauRendezvousTitre;
        set => SetProperty(ref _nouveauRendezvousTitre, value);
    }

    private DateTime _nouveauRendezvousDate = DateTime.Today.AddHours(10);
    public DateTime NouveauRendezvousDate
    {
        get => _nouveauRendezvousDate;
        set => SetProperty(ref _nouveauRendezvousDate, value);
    }

    public ICommand AjouterRendezvousCommand { get; }

    public CalendrierViewModel(IDepotLocal depot)
    {
        _depot = depot;
        AjouterRendezvousCommand = new RelayCommand(AjouterRendezvous);
        Recharger();
    }

    public void Recharger()
    {
        Evenements.Clear();
        // Le calendrier affiche les rendez-vous ET les échéances financières datées (§5.4)
        var dates = _depot.ListerElements()
            .Where(e => e.DateDebut.HasValue || e.Type == TypeElement.Rendezvous)
            .OrderBy(e => e.DateDebut ?? e.DateCreation);

        foreach (var elem in dates)
        {
            Evenements.Add(elem);
        }
    }

    private void AjouterRendezvous()
    {
        if (string.IsNullOrWhiteSpace(NouveauRendezvousTitre))
            return;

        var rdv = new Element
        {
            Titre = NouveauRendezvousTitre.Trim(),
            Type = TypeElement.Rendezvous,
            DateDebut = new DateTimeOffset(NouveauRendezvousDate, TimeSpan.Zero),
            Fuseau = "Europe/Paris",
            Statut = StatutElement.Planifie
        };

        _depot.EnregistrerElement(rdv);
        NouveauRendezvousTitre = string.Empty;
        Recharger();
    }
}
