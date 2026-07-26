using DeuxiemeCerveau.Presentation.VueModeles;
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

        // Relire le compte hors du constructeur : l'appel MSAL est asynchrone.
        Loaded += async (_, _) => await modele.RelireCompte();
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
        Zone.BudgetProjete => new VueBudget(Modele.Budget),
        Zone.Calendrier => BrancherCalendrier(new VueCalendrier(Modele.Calendrier, Modele.Categories)),
        Zone.Finances => BrancherSaisie(new VueFinances(Modele.Finances)),
        Zone.Notes => new VueNotes(Modele.Notes),
        Zone.Corbeille => new VueCorbeille(Modele.Corbeille),
        Zone.Projets => new VueProjets(Modele.Projets),
        _ => new VueAChantier(zone),
    };

    /// <summary>
    /// Relie le bouton « Ajouter » de la vue au formulaire de saisie, qui vit ici : il doit se
    /// superposer à l'écran entier, pas au seul panneau qui l'a déclenché.
    /// </summary>
    private VueFinances BrancherSaisie(VueFinances vue)
    {
        // Finances n'offre que l'argent et les envies : un rendez-vous se plane au Calendrier (§5.4).
        vue.Ajouter += () => Modele.Saisie.OuvrirFinancesCommand.Execute(null);
        return vue;
    }

    private VueCalendrier BrancherCalendrier(VueCalendrier vue)
    {
        vue.NouvelElement += () => Modele.Saisie.OuvrirCalendrierCommand.Execute(null);
        return vue;
    }

    private VueAujourdhui Brancher(VueAujourdhui vue, VueModeleAujourdhui modele)
    {
        vue.Brancher(modele);
        // L'accueil est le seul écran qui voit tout : sa saisie offre donc tous les types.
        vue.Ajouter += () => Modele.Saisie.Ouvrir();
        return vue;
    }
}
