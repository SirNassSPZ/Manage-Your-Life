using DeuxiemeCerveau.Presentation.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class VueFinances : UserControl
{
    public VueFinances(VueModeleFinances modele)
    {
        // Le modèle DOIT exister avant InitializeComponent : x:Bind est résolu là.
        Modele = modele;
        InitializeComponent();

        // La liste du mois est locale et déjà complète ; le chiffre de tête vient du serveur et
        // n'est jamais attendu (règle 9, filet 1).
        Loaded += async (_, _) => await modele.ChargerCloture();
    }

    public VueModeleFinances Modele { get; }

    /// <summary>
    /// Ouvre la saisie. Portée par la coquille et non par cette vue : le même formulaire sert
    /// partout, et il doit se superposer à l'écran entier, pas au seul panneau des finances.
    /// </summary>
    public event Action? Ajouter;

    private void SurAjouter(object envoyeur, Microsoft.UI.Xaml.RoutedEventArgs args) => Ajouter?.Invoke();
}
