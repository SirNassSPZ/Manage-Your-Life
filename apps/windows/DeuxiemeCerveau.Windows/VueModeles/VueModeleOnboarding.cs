using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Windows.Presentation;

namespace DeuxiemeCerveau.Windows.VueModeles;

/// <summary>
/// Une charge ou un revenu proposé au démarrage (D-018, ajout 1). Un preset n'est QUE du contenu
/// de départ : il produit un Élément parfaitement standard (§3.1), rien de neuf dans le schéma.
/// </summary>
public sealed partial class LignePreset : ObservableObject
{
    public required ModeleDepart Modele { get; init; }

    public string Titre => Modele.Titre;
    public bool EstRevenu => Modele.Sens == Sens.Entree;

    // bool? et non bool : CheckBox.IsChecked est nullable, et une liaison classique bool -> bool?
    // laisse la case en état indéterminé. Faire correspondre les types règle le problème.
    [ObservableProperty]
    private bool? _retenu = false;

    [ObservableProperty]
    private string _montant = "";

    [ObservableProperty]
    private int _jour = 1;
}

/// <summary>
/// Onboarding en trois gestes : poser le solde, déclarer un ou deux revenus, cocher ses charges.
/// Ne fait qu'appeler <see cref="ServiceDemarrage"/> et <see cref="ServiceSaisie"/>.
/// </summary>
public sealed partial class VueModeleOnboarding : ObservableObject
{
    private readonly Composition _composition;

    public VueModeleOnboarding(Composition composition)
    {
        _composition = composition;
        var modeles = composition.Demarrage.Modeles;
        foreach (var modele in modeles.Where(m => m.Sens == Sens.Entree))
            Revenus.Add(new LignePreset { Modele = modele, Retenu = true });
        foreach (var modele in modeles.Where(m => m.Sens == Sens.Sortie))
            Charges.Add(new LignePreset { Modele = modele });
    }

    public ObservableCollection<LignePreset> Revenus { get; } = [];
    public ObservableCollection<LignePreset> Charges { get; } = [];

    [ObservableProperty]
    private string _solde = "";

    [ObservableProperty]
    private string? _erreur;

    /// <summary>Levé quand l'onboarding est terminé, pour que la coquille rebascule sur l'accueil.</summary>
    public event Action? Termine;

    public void Valider()
    {
        Erreur = null;

        if (!Format.TryCentimes(Solde, out var centimes))
        {
            Erreur = "Indique le montant dont tu disposes aujourd'hui, par exemple 2 480,00.";
            return;
        }

        var aujourdhui = DateOnly.FromDateTime(DateTime.Now);

        _composition.Acces.Ecrire(() =>
        {
            // Le solde de référence d'abord : sans point de départ, aucune projection n'est possible (§3.4).
            var pose = _composition.Demarrage.DefinirSoldeReference(centimes, aujourdhui);
            if (!pose.Reussi)
            {
                Erreur = string.Join(" · ", pose.Erreurs.Select(e => e.Message));
                return;
            }

            foreach (var ligne in Revenus.Concat(Charges))
            {
                if (ligne.Retenu != true || !Format.TryCentimes(ligne.Montant, out var montant) || montant <= 0) continue;

                var jour = Math.Clamp(ligne.Jour, 1, 28);
                var premiere = PremiereEcheance(aujourdhui, jour);
                var element = ligne.Modele.Composer(montant, premiere);

                var resultat = _composition.Saisie.Enregistrer(element, EntiteSynchro.Element);
                if (!resultat.Reussi)
                    Erreur = $"« {ligne.Titre} » n'a pas pu être enregistré : "
                        + string.Join(" · ", resultat.Erreurs.Select(e => e.Message));
            }
        });

        if (Erreur is null) Termine?.Invoke();
    }

    /// <summary>Prochaine occurrence du jour choisi : ce mois-ci s'il est à venir, sinon le mois prochain.</summary>
    private static DateTimeOffset PremiereEcheance(DateOnly aujourdhui, int jour)
    {
        var candidat = new DateOnly(aujourdhui.Year, aujourdhui.Month, jour);
        if (candidat < aujourdhui) candidat = candidat.AddMonths(1);
        return new DateTimeOffset(candidat.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }
}
