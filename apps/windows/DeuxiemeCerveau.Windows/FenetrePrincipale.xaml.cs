using DeuxiemeCerveau.Windows.VueModeles;
using DeuxiemeCerveau.Windows.Vues;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeuxiemeCerveau.Windows;

public sealed partial class FenetrePrincipale : Window
{
    public FenetrePrincipale(Composition composition)
    {
        InitializeComponent();

        Modele = new VueModeleCoquille(composition);
        Contenu = new Coquille(Modele);
        Hote.Children.Add(Contenu);

        Title = "Deuxième Cerveau";
        AppWindow.Resize(new SizeInt32(1280, 860));
    }

    public VueModeleCoquille Modele { get; }

    /// <summary>Racine visuelle, exposée pour la capture de vérification (mode --capture).</summary>
    public Coquille Contenu { get; }
}
