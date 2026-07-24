using System.Collections.ObjectModel;
using System.Windows.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class FiltreCategorieModel : ViewModelBase
{
    public Categorie Categorie { get; }

    private bool _estActif = true;
    public bool EstActif
    {
        get => _estActif;
        set { SetProperty(ref _estActif, value); OnChangement?.Invoke(); }
    }

    public Action? OnChangement { get; set; }

    public FiltreCategorieModel(Categorie categorie)
    {
        Categorie = categorie;
    }
}

public sealed class CalendrierViewModel : ViewModelBase
{
    private readonly IDepotLocal _depot;

    public ObservableCollection<Element> Evenements { get; } = new();
    public ObservableCollection<FiltreCategorieModel> FiltresCategories { get; } = new();

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

    private string _recurrenceSelectionnee = "Aucune";
    public string RecurrenceSelectionnee
    {
        get => _recurrenceSelectionnee;
        set => SetProperty(ref _recurrenceSelectionnee, value);
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
        FiltresCategories.Clear();
        var cats = _depot.ListerCategories();
        foreach (var c in cats)
        {
            var filtre = new FiltreCategorieModel(c) { OnChangement = FiltrerEvenements };
            FiltresCategories.Add(filtre);
        }

        FiltrerEvenements();
    }

    private void FiltrerEvenements()
    {
        Evenements.Clear();
        var idsCatsActives = FiltresCategories.Where(f => f.EstActif).Select(f => f.Categorie.Id).ToHashSet();

        var dates = _depot.ListerElements()
            .Where(e => (e.DateDebut.HasValue || e.Type == TypeElement.Rendezvous) && !e.Supprime)
            .Where(e => e.Categories.Count == 0 || e.Categories.Any(c => idsCatsActives.Contains(c)))
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

        string? rrule = RecurrenceSelectionnee switch
        {
            "Quotidienne" => "FREQ=DAILY",
            "Hebdomadaire" => "FREQ=WEEKLY",
            "Mensuelle" => "FREQ=MONTHLY",
            _ => null
        };

        var rdv = new Element
        {
            Titre = NouveauRendezvousTitre.Trim(),
            Type = TypeElement.Rendezvous,
            DateDebut = new DateTimeOffset(NouveauRendezvousDate, TimeSpan.Zero),
            Fuseau = "Europe/Paris",
            Recurrence = rrule,
            Statut = StatutElement.Planifie
        };

        _depot.EnregistrerElement(rdv);
        NouveauRendezvousTitre = string.Empty;
        RecurrenceSelectionnee = "Aucune";
        Recharger();
    }
}
