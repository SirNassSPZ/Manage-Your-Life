using DeuxiemeCerveau.Windows.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class VueBudget : UserControl
{
    public VueBudget(VueModeleBudget modele)
    {
        Modele = modele;
        InitializeComponent();

        // Le premier appel peut réveiller la base serverless : on le lance dès l'affichage,
        // sans jamais bloquer l'interface.
        Loaded += async (_, _) => await modele.Charger();
    }

    public VueModeleBudget Modele { get; }
}
