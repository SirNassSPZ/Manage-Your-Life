using DeuxiemeCerveau.Windows.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class VueAujourdhui : UserControl
{
    public VueAujourdhui() => InitializeComponent();

    /// <summary>
    /// Posé par la coquille juste après la construction. La vue ne fabrique pas son modèle :
    /// le graphe de services est unique et composé au démarrage.
    /// </summary>
    public VueModeleAujourdhui Modele { get; private set; } = null!;

    public void Brancher(VueModeleAujourdhui modele)
    {
        Modele = modele;
        Bindings.Update();
    }
}
