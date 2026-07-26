using DeuxiemeCerveau.Presentation;
using DeuxiemeCerveau.Presentation.VueModeles;
using DeuxiemeCerveau.Windows.Vues;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeuxiemeCerveau.Windows;

public sealed partial class FenetrePrincipale : Window
{
    public FenetrePrincipale(Composition composition)
    {
        InitializeComponent();

        // Le HWND est résolu à l'appel, pas maintenant : les boîtes de dialogue de fichier en
        // application non empaquetée en ont besoin, et il n'existe qu'une fois la fenêtre montée.
        var selecteur = new Services.SelecteurFichierWindows(
            () => WinRT.Interop.WindowNative.GetWindowHandle(this));

        Modele = new VueModeleCoquille(composition, selecteur);
        Contenu = new Coquille(Modele);
        Hote.Children.Add(Contenu);

        Title = "Deuxième Cerveau";
        AppWindow.Resize(new SizeInt32(1280, 860));
    }

    public VueModeleCoquille Modele { get; }

    /// <summary>Racine visuelle, exposée pour la capture de vérification (mode --capture).</summary>
    public Coquille Contenu { get; }
}
