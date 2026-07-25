using DeuxiemeCerveau.Presentation.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class VueCorbeille : UserControl
{
    public VueCorbeille(VueModeleCorbeille modele)
    {
        Modele = modele;
        InitializeComponent();
        RacineCorbeille.DataContext = this;
    }

    public VueModeleCorbeille Modele { get; }
}
