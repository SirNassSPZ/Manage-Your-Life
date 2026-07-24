using System.Collections.ObjectModel;
using System.Windows.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App.ViewModels;

public sealed class EnviesViewModel : ViewModelBase
{
    private readonly IDepotLocal _depot;

    public ObservableCollection<Element> Envies { get; } = new();

    private string _nouvelleEnvieTitre = string.Empty;
    public string NouvelleEnvieTitre
    {
        get => _nouvelleEnvieTitre;
        set => SetProperty(ref _nouvelleEnvieTitre, value);
    }

    private double _nouvelleEnvieMontantEuros;
    public double NouvelleEnvieMontantEuros
    {
        get => _nouvelleEnvieMontantEuros;
        set => SetProperty(ref _nouvelleEnvieMontantEuros, value);
    }

    public ICommand AjouterEnvieCommand { get; }

    public EnviesViewModel(IDepotLocal depot)
    {
        _depot = depot;
        AjouterEnvieCommand = new RelayCommand(AjouterEnvie);
        Recharger();
    }

    public void Recharger()
    {
        Envies.Clear();
        var liste = _depot.ListerElements()
            .Where(e => e.Type == TypeElement.Envie && !e.Supprime)
            .OrderByDescending(e => e.DateCreation);

        foreach (var elem in liste)
        {
            Envies.Add(elem);
        }
    }

    private void AjouterEnvie()
    {
        if (string.IsNullOrWhiteSpace(NouvelleEnvieTitre))
            return;

        var envie = new Element
        {
            Titre = NouvelleEnvieTitre.Trim(),
            Type = TypeElement.Envie,
            MontantCentimes = (long)Math.Round(NouvelleEnvieMontantEuros * 100),
            Devise = "EUR",
            Statut = StatutElement.Idee
        };

        _depot.EnregistrerElement(envie);
        NouvelleEnvieTitre = string.Empty;
        NouvelleEnvieMontantEuros = 0;
        Recharger();
    }
}
