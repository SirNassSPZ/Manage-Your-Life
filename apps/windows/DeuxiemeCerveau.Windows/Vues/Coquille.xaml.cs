using DeuxiemeCerveau.Windows.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class Coquille : UserControl
{
    public Coquille(VueModeleCoquille modele)
    {
        // Le modèle DOIT exister avant InitializeComponent : x:Bind est résolu là.
        Modele = modele;
        InitializeComponent();
        Racine.DataContext = modele;
        Accueil.Brancher(modele.Accueil);
    }

    public VueModeleCoquille Modele { get; }
}
