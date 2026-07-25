using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.App.Synchro;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>
/// Un mois de la projection, déjà mis en forme. <paramref name="Largeur"/> est en PIXELS sur une
/// piste fixe : la vue n'a ainsi ni convertisseur ni arithmétique à faire.
/// </summary>
public sealed record LigneMois(
    string Mois,
    string Cloture,
    bool Decouvert,
    bool AvantReference,
    double Largeur);

/// <summary>
/// Budget projeté (§5.1) — le seul écran dont le chiffre vient ENTIÈREMENT du serveur.
/// <para>
/// L'algorithme vit dans l'API et nulle part ailleurs (règle 9) : cette vue ne recalcule rien,
/// n'additionne rien, ne met rien en cache. Elle appelle <c>IClientApi.Projeter</c> et affiche.
/// </para>
/// </summary>
public sealed partial class VueModeleBudget : ObservableObject
{
    private const int HorizonMois = 12;

    /// <summary>Largeur de la piste des barres, en pixels — doit coller au XAML.</summary>
    private const double PisteBarre = 320;

    private readonly Composition _composition;

    public VueModeleBudget(Composition composition) => _composition = composition;

    public ObservableCollection<LigneMois> Mois { get; } = [];

    [ObservableProperty]
    private bool _chargement;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string _entete = "—";

    [RelayCommand]
    public async Task Charger()
    {
        if (Chargement) return;
        Chargement = true;
        Message = null;

        try
        {
            if (!_composition.Options.Api.EstConfiguree)
            {
                Echouer("Aucune adresse d'API configurée : la projection est calculée par le serveur (§5.1).");
                return;
            }

            // Le premier appel peut réveiller la base serverless — jusqu'à une minute (§10.1).
            var projection = await _composition.Api.Projeter(HorizonMois);
            Remplir(projection);
        }
        catch (ErreurSynchro erreur) when (erreur.Statut is 401 or 403)
        {
            Echouer("Connecte-toi pour voir ta projection : elle est calculée par le serveur.");
        }
        catch (ErreurSynchro erreur)
        {
            Echouer($"Le serveur a répondu {erreur.Statut}. Réessaie dans un instant.");
        }
        catch (Exception)
        {
            Echouer("Serveur injoignable. Tes données locales restent intactes — réessaie plus tard.");
        }
        finally
        {
            Chargement = false;
        }
    }

    private void Echouer(string message)
    {
        Mois.Clear();
        Entete = "—";
        Message = message;
    }

    private void Remplir(ReponseProjectionClient projection)
    {
        Mois.Clear();

        var connus = projection.Mois.Where(m => !m.AvantReference && m.ClotureCentimes is not null).ToList();
        if (connus.Count == 0)
        {
            Echouer("Aucun mois projetable : pose d'abord ton solde de référence (§3.4).");
            return;
        }

        Entete = Format.EurosRelatif(connus[0].ClotureCentimes!.Value);

        // Barres relatives au plus haut solde positif ; un mois à découvert n'a pas de barre.
        var plafond = Math.Max(1, connus.Max(m => Math.Max(0, m.ClotureCentimes!.Value)));

        foreach (var mois in projection.Mois)
        {
            var cloture = mois.ClotureCentimes;
            Mois.Add(new LigneMois(
                Mois: Format.MoisAbregeTitre(mois.Mois),
                Cloture: cloture is null ? "—" : Format.EurosRelatif(cloture.Value),
                Decouvert: mois.Decouvert || cloture < 0,
                AvantReference: mois.AvantReference,
                Largeur: cloture is > 0 ? Math.Round(cloture.Value * PisteBarre / plafond, 1) : 0));
        }

        var rouges = Mois.Count(m => m.Decouvert);
        Message = rouges switch
        {
            0 => null,
            1 => "Un mois passe dans le rouge — il ressort ci-dessous.",
            _ => $"{rouges} mois passent dans le rouge — ils ressortent ci-dessous.",
        };
    }

}
