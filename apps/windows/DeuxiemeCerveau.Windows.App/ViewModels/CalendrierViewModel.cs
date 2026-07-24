using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class JourCalendrierModel : ViewModelBase
{
    public DateTime Date { get; }
    public int JourNumero => Date.Day;
    public bool EstMoisCourant { get; }
    public bool EstAujourdhui { get; }

    public ObservableCollection<Element> Evenements { get; } = new();

    public JourCalendrierModel(DateTime date, bool estMoisCourant, bool estAujourdhui)
    {
        Date = date;
        EstMoisCourant = estMoisCourant;
        EstAujourdhui = estAujourdhui;
    }
}

public sealed class CalendrierViewModel : ViewModelBase
{
    private readonly IDepotLocal _depot;

    private DateTime _dateAffichage = DateTime.Today;

    public string MoisCourantTitre => _dateAffichage.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR")).ToUpperFirst();

    public ObservableCollection<JourCalendrierModel> GrilleJours { get; } = new();
    public ObservableCollection<Element> EvenementsDuJour { get; } = new();
    public ObservableCollection<FiltreCategorieModel> FiltresCategories { get; } = new();

    private JourCalendrierModel? _jourSelectionne;
    public JourCalendrierModel? JourSelectionne
    {
        get => _jourSelectionne;
        set
        {
            if (SetProperty(ref _jourSelectionne, value))
                MettreAJourEvenementsDuJour();
        }
    }

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

    public ICommand MoisPrecedentCommand { get; }
    public ICommand MoisSuivantCommand { get; }
    public ICommand AujourdhuiCommand { get; }
    public ICommand AjouterRendezvousCommand { get; }

    public CalendrierViewModel(IDepotLocal depot)
    {
        _depot = depot;

        MoisPrecedentCommand = new RelayCommand(() => ChangerMois(-1));
        MoisSuivantCommand = new RelayCommand(() => ChangerMois(1));
        AujourdhuiCommand = new RelayCommand(() => { _dateAffichage = DateTime.Today; Recharger(); });
        AjouterRendezvousCommand = new RelayCommand(AjouterRendezvous);

        Recharger();
    }

    private void ChangerMois(int delta)
    {
        _dateAffichage = _dateAffichage.AddMonths(delta);
        OnPropertyChanged(nameof(MoisCourantTitre));
        ConstruireGrilleMois();
    }

    public void Recharger()
    {
        OnPropertyChanged(nameof(MoisCourantTitre));

        FiltresCategories.Clear();
        var cats = _depot.ListerCategories();
        foreach (var c in cats)
        {
            var filtre = new FiltreCategorieModel(c) { OnChangement = ConstruireGrilleMois };
            FiltresCategories.Add(filtre);
        }

        ConstruireGrilleMois();
    }

    private void ConstruireGrilleMois()
    {
        GrilleJours.Clear();

        var idsCatsActives = FiltresCategories.Where(f => f.EstActif).Select(f => f.Categorie.Id).ToHashSet();

        var tousElements = _depot.ListerElements()
            .Where(e => (e.DateDebut.HasValue || e.Type == TypeElement.Rendezvous) && !e.Supprime)
            .Where(e => e.Categories.Count == 0 || e.Categories.Any(c => idsCatsActives.Contains(c)))
            .ToList();

        var premierDuMois = new DateTime(_dateAffichage.Year, _dateAffichage.Month, 1);
        int decallageLundi = ((int)premierDuMois.DayOfWeek + 6) % 7; // Lundi = 0
        var debutGrille = premierDuMois.AddDays(-decallageLundi);

        JourCalendrierModel? jourAuj = null;

        for (int i = 0; i < 35; i++)
        {
            var dateJour = debutGrille.AddDays(i);
            bool estMois = dateJour.Month == _dateAffichage.Month;
            bool estAuj = dateJour.Date == DateTime.Today;

            var j = new JourCalendrierModel(dateJour, estMois, estAuj);

            var elemsDuJour = tousElements.Where(e =>
                (e.DateDebut.HasValue && e.DateDebut.Value.Date == dateJour.Date) ||
                (e.DateCreation.Date == dateJour.Date && e.Type == TypeElement.Rendezvous)
            );

            foreach (var elem in elemsDuJour)
            {
                j.Evenements.Add(elem);
            }

            GrilleJours.Add(j);

            if (estAuj)
                jourAuj = j;
        }

        JourSelectionne = jourAuj ?? GrilleJours.FirstOrDefault(j => j.EstMoisCourant);
    }

    private void MettreAJourEvenementsDuJour()
    {
        EvenementsDuJour.Clear();
        if (JourSelectionne is null) return;

        foreach (var elem in JourSelectionne.Evenements)
        {
            EvenementsDuJour.Add(elem);
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

public static class StringExtensions
{
    public static string ToUpperFirst(this string input) =>
        string.IsNullOrEmpty(input) ? input : char.ToUpper(input[0]) + input[1..];
}
