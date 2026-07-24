using DeuxiemeCerveau.Core.Modele;

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
