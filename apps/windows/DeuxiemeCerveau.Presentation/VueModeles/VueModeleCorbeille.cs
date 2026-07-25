using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Une entrée de corbeille — Élément ou catégorie, restaurable d'un geste (§5.6).</summary>
public sealed partial class LigneCorbeille : ObservableObject
{
    public required Guid Id { get; init; }
    public required EntiteSynchro Entite { get; init; }
    public required string Titre { get; init; }

    /// <summary>« Facture », « Note », « Calendrier »… — dire CE qu'on restaure.</summary>
    public required string Nature { get; init; }

    public required string SupprimeLe { get; init; }
    public required TypeElement Type { get; init; }

    /// <summary>Vrai quand la purge de cette ligne attend une confirmation explicite (§5.6).</summary>
    [ObservableProperty]
    private bool _confirmationPurge;
}

/// <summary>
/// Corbeille (§5.6) — conséquence visible du filet 2 : rien n'est effacé, seulement marqué.
/// <para>
/// La <b>purge</b> est la seule destruction réelle de l'application. Elle exige donc une
/// confirmation explicite, ligne par ligne — pas de « tout vider » d'un clic : le geste doit
/// coûter autant que ce qu'il détruit.
/// </para>
/// </summary>
public sealed partial class VueModeleCorbeille : ObservableObject
{
    private readonly Composition _composition;

    public VueModeleCorbeille(Composition composition)
    {
        _composition = composition;
        Charger();
    }

    /// <summary>Prévient la coquille : restaurer un calendrier le remet dans la barre latérale.</summary>
    public Action? ApresChangement { get; set; }

    public ObservableCollection<LigneCorbeille> Lignes { get; } = [];

    [ObservableProperty]
    private bool _vide;

    [ObservableProperty]
    private string? _message;

    /// <summary>Restauration en un geste (§5.6) — sans confirmation : elle ne détruit rien.</summary>
    [RelayCommand]
    private void Restaurer(LigneCorbeille ligne)
    {
        var resultat = _composition.Acces.Lire(() =>
            _composition.Saisie.Restaurer(ligne.Entite, ligne.Id));

        Message = resultat.Reussi
            ? $"« {ligne.Titre} » est de retour."
            : resultat.Erreurs.FirstOrDefault()?.Message;

        Recharger();
    }

    /// <summary>Premier temps de la purge : demander. Le second temps seul détruit.</summary>
    [RelayCommand]
    private void DemanderPurge(LigneCorbeille ligne)
    {
        foreach (var autre in Lignes) autre.ConfirmationPurge = false;
        ligne.ConfirmationPurge = true;
        Message = null;
    }

    [RelayCommand]
    private void AnnulerPurge(LigneCorbeille ligne) => ligne.ConfirmationPurge = false;

    /// <summary>
    /// Second temps : la destruction. Locale immédiate, puis mise en file pour le serveur, qui
    /// arbitre (D-010) — si l'entité a été restaurée ailleurs entre-temps, il refuse et le prochain
    /// pull la rend. La conservation gagne toujours la course.
    /// </summary>
    [RelayCommand]
    private void ConfirmerPurge(LigneCorbeille ligne)
    {
        _composition.Acces.Lire(() =>
        {
            _composition.Purge.Purger(ligne.Entite, ligne.Id);
            return true;
        });

        Message = $"« {ligne.Titre} » est définitivement supprimé.";
        Recharger();
    }

    private void Recharger()
    {
        Charger();
        ApresChangement?.Invoke();
    }

    public void Charger()
    {
        var (elements, categories) = _composition.Acces.Lire(() =>
            (_composition.Lecture.Corbeille(), _composition.Lecture.CorbeilleCategories()));

        Lignes.Clear();

        foreach (var element in elements)
            Lignes.Add(new LigneCorbeille
            {
                Id = element.Id,
                Entite = EntiteSynchro.Element,
                Titre = string.IsNullOrWhiteSpace(element.Titre) ? "(sans titre)" : element.Titre,
                Nature = Nature(element.Type),
                Type = element.Type,
                SupprimeLe = Depuis(element.DateSuppression ?? element.DateModification),
            });

        // Le filet 2 vaut pour toute entité synchronisée (D-006), pas seulement l'Élément.
        foreach (var categorie in categories)
            Lignes.Add(new LigneCorbeille
            {
                Id = categorie.Id,
                Entite = EntiteSynchro.Categorie,
                Titre = categorie.Nom,
                Nature = "Calendrier",
                Type = TypeElement.Note,   // pastille neutre : une catégorie n'a pas de type d'Élément
                SupprimeLe = Depuis(categorie.DateSuppression ?? categorie.DateModification),
            });

        Vide = Lignes.Count == 0;
    }

    private static string Nature(TypeElement type) => type switch
    {
        TypeElement.Facture => "Facture",
        TypeElement.Paiement => "Paiement",
        TypeElement.Revenu => "Revenu",
        TypeElement.Rendezvous => "Rendez-vous",
        TypeElement.Tache => "Tâche",
        TypeElement.Envie => "Envie",
        TypeElement.Note => "Note",
        _ => "Élément",
    };

    /// <summary>« aujourd'hui », « hier », « il y a 3 jours » — plus parlant qu'une date brute ici.</summary>
    private static string Depuis(DateTimeOffset instant)
    {
        var jours = (DateOnly.FromDateTime(DateTime.Now).DayNumber
            - DateOnly.FromDateTime(instant.ToLocalTime().DateTime).DayNumber);

        return jours switch
        {
            <= 0 => "Supprimé aujourd'hui",
            1 => "Supprimé hier",
            < 30 => $"Supprimé il y a {jours} jours",
            _ => "Supprimé le " + Format.JourLong(DateOnly.FromDateTime(instant.ToLocalTime().DateTime)),
        };
    }
}
