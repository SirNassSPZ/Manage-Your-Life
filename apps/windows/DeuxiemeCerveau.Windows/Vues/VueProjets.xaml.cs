using DeuxiemeCerveau.Presentation.VueModeles;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DeuxiemeCerveau.Windows.Vues;

/// <summary>
/// Projets personnels et leurs tâches (§5.3, V1 depuis D-027). N'affiche et ne saisit que : toute
/// la décision vit dans <see cref="VueModeleProjets"/>, donc dans la couche testée (D-022).
/// </summary>
public sealed partial class VueProjets : UserControl
{
    public VueProjets(VueModeleProjets modele)
    {
        // Le modèle DOIT exister avant InitializeComponent : x:Bind est résolu là.
        Modele = modele;
        InitializeComponent();
        RacineProjets.DataContext = this;
    }

    public VueModeleProjets Modele { get; }

    // Entrée valide la saisie. Deux champs, deux gestes : taper puis Entrée, sans viser un bouton.
    private void SurToucheNouveauProjet(object envoyeur, KeyRoutedEventArgs args)
    {
        if (args.Key != global::Windows.System.VirtualKey.Enter) return;
        Modele.CreerCommand.Execute(null);
        args.Handled = true;
    }

    private void SurToucheNouvelleTache(object envoyeur, KeyRoutedEventArgs args)
    {
        if (args.Key != global::Windows.System.VirtualKey.Enter) return;
        Modele.AjouterTacheCommand.Execute(null);
        args.Handled = true;
    }
}
