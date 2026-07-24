using System.Windows.Input;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public FinancesViewModel FinancesVM { get; }
    public CalendrierViewModel CalendrierVM { get; }
    public NotesViewModel NotesVM { get; }
    public CorbeilleViewModel CorbeilleVM { get; }
    public ReglagesViewModel ReglagesVM { get; }

    private ViewModelBase _vueCourante;
    public ViewModelBase VueCourante
    {
        get => _vueCourante;
        set => SetProperty(ref _vueCourante, value);
    }

    public ICommand NaviguerFinancesCommand { get; }
    public ICommand NaviguerCalendrierCommand { get; }
    public ICommand NaviguerNotesCommand { get; }
    public ICommand NaviguerCorbeilleCommand { get; }
    public ICommand NaviguerReglagesCommand { get; }

    public MainViewModel(IDepotLocal depot)
    {
        FinancesVM = new FinancesViewModel(depot);
        CalendrierVM = new CalendrierViewModel(depot);
        NotesVM = new NotesViewModel(depot);
        CorbeilleVM = new CorbeilleViewModel(depot);
        ReglagesVM = new ReglagesViewModel(depot);

        _vueCourante = FinancesVM;

        NaviguerFinancesCommand = new RelayCommand(() => { FinancesVM.Recharger(); VueCourante = FinancesVM; });
        NaviguerCalendrierCommand = new RelayCommand(() => { CalendrierVM.Recharger(); VueCourante = CalendrierVM; });
        NaviguerNotesCommand = new RelayCommand(() => { NotesVM.Recharger(); VueCourante = NotesVM; });
        NaviguerCorbeilleCommand = new RelayCommand(() => { CorbeilleVM.Recharger(); VueCourante = CorbeilleVM; });
        NaviguerReglagesCommand = new RelayCommand(() => { ReglagesVM.Recharger(); VueCourante = ReglagesVM; });
    }
}
