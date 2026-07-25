using DeuxiemeCerveau.Presentation.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class VueCalendrier : UserControl
{
    /// <summary>
    /// La zone Calendrier porte trois lectures : la grille du mois, les sept prochains jours, et
    /// la gestion des calendriers eux-mêmes — catégorie = calendrier, une seule notion (§3.3).
    /// D'où deux modèles de vue plutôt qu'un fourre-tout.
    /// </summary>
    public VueCalendrier(VueModeleCalendrier modele, VueModeleCategories categories)
    {
        // Les modèles DOIVENT exister avant InitializeComponent : x:Bind est résolu là.
        Modele = modele;
        Categories = categories;
        InitializeComponent();

        // Les gabarits d'items utilisent {Binding} (ItemsRepeater imbriqué) : ils remontent au
        // DataContext de la racine pour atteindre les commandes.
        RacineCal.DataContext = this;
    }

    public VueModeleCalendrier Modele { get; }

    public VueModeleCategories Categories { get; }
}
