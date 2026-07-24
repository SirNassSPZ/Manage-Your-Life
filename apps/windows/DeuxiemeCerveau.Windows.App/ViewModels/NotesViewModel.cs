using System.Collections.ObjectModel;
using System.Windows.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class NotesViewModel : ViewModelBase
{
    private readonly IDepotLocal _depot;

    public ObservableCollection<Element> Notes { get; } = new();

    private string _nouvelleNoteTexte = string.Empty;
    public string NouvelleNoteTexte
    {
        get => _nouvelleNoteTexte;
        set => SetProperty(ref _nouvelleNoteTexte, value);
    }

    public ICommand AjouterNoteCommand { get; }

    public NotesViewModel(IDepotLocal depot)
    {
        _depot = depot;
        AjouterNoteCommand = new RelayCommand(AjouterNote);
        Recharger();
    }

    public void Recharger()
    {
        Notes.Clear();
        var notes = _depot.ListerElements()
            .Where(e => e.Type == TypeElement.Note)
            .OrderByDescending(e => e.DateCreation);

        foreach (var note in notes)
        {
            Notes.Add(note);
        }
    }

    private void AjouterNote()
    {
        if (string.IsNullOrWhiteSpace(NouvelleNoteTexte))
            return;

        var note = new Element
        {
            Titre = NouvelleNoteTexte.Length > 50 ? NouvelleNoteTexte[..50] + "..." : NouvelleNoteTexte,
            Description = NouvelleNoteTexte,
            Type = TypeElement.Note,
            Statut = StatutElement.Active
        };

        _depot.EnregistrerElement(note);
        NouvelleNoteTexte = string.Empty;
        Recharger();
    }
}
