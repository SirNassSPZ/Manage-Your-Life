using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Une note dans la liste de gauche.</summary>
public sealed record LigneNote(Guid Id, string Titre, string Apercu, string Modifiee);

/// <summary>
/// Note libre (§5.5) — un espace de texte, sans structure imposée.
/// <para>
/// « Un brouillon ne se perd jamais » : l'enregistrement est <b>local d'abord</b> et se déclenche
/// avant toute perte possible — en changeant de note, en en créant une autre, en quittant la vue.
/// Aucune intelligence en V1 : convertir une note en Élément typé est explicitement V3.
/// </para>
/// </summary>
public sealed partial class VueModeleNotes : ObservableObject
{
    private readonly Composition _composition;

    public VueModeleNotes(Composition composition)
    {
        _composition = composition;
        Charger();
    }

    public ObservableCollection<LigneNote> Notes { get; } = [];

    /// <summary>Identifiant de la note ouverte, ou null si aucune.</summary>
    [ObservableProperty]
    private Guid? _ouverte;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AUneNoteOuverte))]
    private string _titre = "";

    [ObservableProperty]
    private string _texte = "";

    [ObservableProperty]
    private bool _aUneNoteOuverte;

    [ObservableProperty]
    private bool _aucuneNote;

    /// <summary>« Modifiée à l'instant » / « Enregistrée » — l'utilisateur doit voir que c'est sûr.</summary>
    [ObservableProperty]
    private string _etat = "";

    [ObservableProperty]
    private bool _modifiee;

    partial void OnTitreChanged(string value) => Marquer();

    partial void OnTexteChanged(string value) => Marquer();

    private void Marquer()
    {
        if (!AUneNoteOuverte) return;
        Modifiee = true;
        Etat = "Modifiée — non enregistrée";
    }

    [RelayCommand]
    private void Nouvelle()
    {
        EnregistrerSiBesoin();

        var note = new Element { Type = TypeElement.Note, Titre = "Nouvelle note", Statut = StatutElement.Active };
        var resultat = _composition.Acces.Lire(() =>
            _composition.Saisie.Enregistrer(note, EntiteSynchro.Element));

        if (!resultat.Reussi)
        {
            Etat = resultat.Erreurs.FirstOrDefault()?.Message ?? "Création refusée.";
            return;
        }

        Charger();
        Ouvrir(note.Id);
    }

    /// <summary>Ouvre une note. Enregistre la précédente d'abord — on ne perd jamais un brouillon.</summary>
    [RelayCommand]
    private void OuvrirNote(Guid id)
    {
        if (Ouverte == id) return;
        EnregistrerSiBesoin();
        Ouvrir(id);
    }

    [RelayCommand]
    private void Enregistrer() => EnregistrerSiBesoin(force: true);

    /// <summary>Met la note à la corbeille (filet 2) — elle reste restaurable.</summary>
    [RelayCommand]
    private void Supprimer()
    {
        if (Ouverte is not { } id) return;

        var resultat = _composition.Acces.Lire(() =>
            _composition.Saisie.Supprimer(EntiteSynchro.Element, id));

        if (!resultat.Reussi)
        {
            Etat = resultat.Erreurs.FirstOrDefault()?.Message ?? "Suppression refusée.";
            return;
        }

        Ouverte = null;
        AUneNoteOuverte = false;
        Titre = "";
        Texte = "";
        Modifiee = false;
        Etat = "Note mise à la corbeille.";
        Charger();
    }

    /// <summary>
    /// Point de sauvegarde unique. Appelé avant chaque transition qui pourrait perdre la saisie —
    /// c'est ce qui rend la promesse du §5.5 tenable sans minuterie.
    /// </summary>
    public void EnregistrerSiBesoin(bool force = false)
    {
        if (Ouverte is not { } id) return;
        if (!Modifiee && !force) return;

        var etat = _composition.Acces.Lire(() => _composition.Depot.Obtenir(EntiteSynchro.Element, id));
        if (etat is null) return;

        // Repartir de l'entité STOCKÉE : un objet neuf écraserait ses champs d'audit (§3.1).
        var note = Core.Json.SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);
        note.Titre = string.IsNullOrWhiteSpace(Titre) ? "(sans titre)" : Titre.Trim();
        note.Description = Texte;

        var resultat = _composition.Acces.Lire(() =>
            _composition.Saisie.Enregistrer(note, EntiteSynchro.Element));

        if (!resultat.Reussi)
        {
            Etat = resultat.Erreurs.FirstOrDefault()?.Message ?? "Enregistrement refusé.";
            return;
        }

        Modifiee = false;
        Etat = "Enregistrée";
        Charger();
    }

    private void Ouvrir(Guid id)
    {
        var etat = _composition.Acces.Lire(() => _composition.Depot.Obtenir(EntiteSynchro.Element, id));
        if (etat is null) return;

        var note = Core.Json.SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);

        Ouverte = id;
        AUneNoteOuverte = true;
        Titre = note.Titre;
        Texte = note.Description ?? "";
        Modifiee = false;
        Etat = "Enregistrée";
    }

    public void Charger()
    {
        var notes = _composition.Acces.Lire(() => _composition.Lecture.Notes());

        Notes.Clear();
        foreach (var note in notes)
            Notes.Add(new LigneNote(
                note.Id,
                string.IsNullOrWhiteSpace(note.Titre) ? "(sans titre)" : note.Titre,
                Apercu(note.Description),
                Format.JourLong(DateOnly.FromDateTime(note.DateModification.ToLocalTime().DateTime))));

        AucuneNote = Notes.Count == 0;

        // La note ouverte a pu partir à la corbeille depuis un autre écran.
        if (Ouverte is { } id && Notes.All(n => n.Id != id))
        {
            Ouverte = null;
            AUneNoteOuverte = false;
            Titre = "";
            Texte = "";
        }
    }

    private static string Apercu(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "Vide";

        var ligne = description.ReplaceLineEndings(" ").Trim();
        return ligne.Length <= 70 ? ligne : ligne[..70].TrimEnd() + "…";
    }
}
