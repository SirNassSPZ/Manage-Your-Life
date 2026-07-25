using DeuxiemeCerveau.Windows.VueModeles;
using Microsoft.UI.Xaml.Controls;

namespace DeuxiemeCerveau.Windows.Vues;

public sealed partial class Coquille : UserControl
{
    private readonly Dictionary<Zone, UserControl> _vues = [];
    private VueOnboarding? _onboarding;

    public Coquille(VueModeleCoquille modele)
    {
        // Le modèle DOIT exister avant InitializeComponent : x:Bind est résolu là.
        Modele = modele;
        InitializeComponent();
        Racine.DataContext = modele;

        modele.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VueModeleCoquille.Zone)) Basculer();
        };

        modele.Onboarding.Termine += () =>
        {
            _onboarding = null;
            modele.Aller(Zone.Aujourdhui);
            Basculer();
        };

        Basculer();
    }

    public VueModeleCoquille Modele { get; }

    /// <summary>
    /// Pose la vue correspondant à la zone active. Les vues sont conservées d'une bascule à
    /// l'autre : elles portent l'état de saisie en cours, qu'on ne veut pas perdre en naviguant.
    /// </summary>
    private void Basculer()
    {
        // Tant que le solde de référence n'est pas posé, aucune projection n'est possible (§3.4) :
        // l'onboarding passe donc devant tout le reste.
        if (Modele.OnboardingRequis())
        {
            Hote.Content = _onboarding ??= new VueOnboarding(Modele.Onboarding);
            return;
        }

        if (!_vues.TryGetValue(Modele.Zone, out var vue))
        {
            vue = Fabriquer(Modele.Zone);
            _vues[Modele.Zone] = vue;
        }

        Hote.Content = vue;
    }

    private UserControl Fabriquer(Zone zone) => zone switch
    {
        Zone.Aujourdhui => Brancher(new VueAujourdhui(), Modele.Accueil),
        _ => new VueAChantier(zone),
    };

    private static VueAujourdhui Brancher(VueAujourdhui vue, VueModeleAujourdhui modele)
    {
        vue.Brancher(modele);
        return vue;
    }
}
