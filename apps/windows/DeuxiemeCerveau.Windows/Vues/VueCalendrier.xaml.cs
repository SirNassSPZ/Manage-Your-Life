using DeuxiemeCerveau.Windows.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class VueCalendrier : UserControl
{
    public VueCalendrier(VueModeleCalendrier modele)
    {
        // Le modèle DOIT exister avant InitializeComponent : x:Bind est résolu là.
        Modele = modele;
        InitializeComponent();
    }

    public VueModeleCalendrier Modele { get; }
}
