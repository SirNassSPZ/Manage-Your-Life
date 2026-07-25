using DeuxiemeCerveau.Presentation.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class VueNotes : UserControl
{
    public VueNotes(VueModeleNotes modele)
    {
        Modele = modele;
        InitializeComponent();
        RacineNotes.DataContext = this;

        // Quitter la vue ne doit jamais coûter une saisie (§5.5).
        Unloaded += (_, _) => modele.EnregistrerSiBesoin();
    }

    public VueModeleNotes Modele { get; }
}
