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
/// <b>Boîte de capture, pas document ouvert.</b> On écrit dans une zone vierge, on enregistre, et
/// la zone <b>se vide</b> : la note rejoint la liste, la place est libre pour la suivante. Cela
/// vaut aussi quand on a rouvert une note pour la corriger. Un texte qui reste après enregistrement
/// laisse croire qu'il n'est pas parti — et pousse à le retaper.
/// </para>
/// <para>
/// « Un brouillon ne se perd jamais » : l'enregistrement est <b>local d'abord</b> et se déclenche
/// avant toute perte possible — en ouvrant une autre note, en vidant la zone, en quittant la vue.
/// Le vidage, lui, <b>suit</b> l'écriture confirmée et ne la précède jamais : vider une zone dont
/// l'enregistrement vient d'être refusé perdrait la note.
/// </para>
/// <para>
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

    /// <summary>
    /// Marque la zone comme modifiée. <b>Sans exiger qu'une note soit ouverte</b> : en boîte de
    /// capture, on écrit d'abord et la note n'existe qu'à l'enregistrement.
    /// </summary>
    private void Marquer()
    {
        Modifiee = true;
        Etat = ZoneVide ? "" : "Non enregistrée";
    }

    /// <summary>Vrai quand il n'y a rien à enregistrer — ni titre, ni texte.</summary>
    private bool ZoneVide => string.IsNullOrWhiteSpace(Titre) && string.IsNullOrWhiteSpace(Texte);

    /// <summary>
    /// Libère la zone pour une nouvelle note. Enregistre d'abord ce qui s'y trouve : le geste veut
    /// dire « je passe à la suivante », jamais « jette ce que je viens d'écrire ».
    /// </summary>
    [RelayCommand]
    private void Nouvelle()
    {
        if (!Ecrire()) return;
        Vider();
    }

    /// <summary>Ouvre une note. Enregistre la zone d'abord — on ne perd jamais un brouillon.</summary>
    [RelayCommand]
    private void OuvrirNote(Guid id)
    {
        if (Ouverte == id) return;
        if (!Ecrire()) return;
        Ouvrir(id);
    }

    /// <summary>
    /// Enregistre, puis <b>vide la zone</b> (§5.5). Le vidage n'a lieu que si l'écriture est
    /// passée : sur refus, le texte reste à l'écran, sans quoi le geste le perdrait.
    /// </summary>
    [RelayCommand]
    private void Enregistrer()
    {
        if (!Ecrire(force: true)) return;
        Vider();
        Etat = "Enregistrée";
    }

    private void Vider()
    {
        Ouverte = null;
        AUneNoteOuverte = false;
        Titre = "";
        Texte = "";
        Modifiee = false;
        Etat = "";
    }

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
    /// c'est ce qui rend la promesse du §5.5 tenable sans minuterie. Conservé sous ce nom : la
    /// coquille l'appelle en quittant la vue.
    /// </summary>
    public void EnregistrerSiBesoin(bool force = false) => Ecrire(force);

    /// <summary>
    /// Écrit la zone dans la base. Crée la note si aucune n'est ouverte — en boîte de capture, la
    /// note n'existe qu'à l'enregistrement. Rend <c>false</c> si l'écriture a été refusée, auquel
    /// cas l'appelant <b>ne doit pas</b> vider la zone.
    /// </summary>
    private bool Ecrire(bool force = false)
    {
        if (!Modifiee && !force) return true;
        if (ZoneVide && Ouverte is null) return true; // rien à enregistrer, et ce n'est pas un échec

        var titre = string.IsNullOrWhiteSpace(Titre) ? "(sans titre)" : Titre.Trim();
        Element note;

        if (Ouverte is { } id)
        {
            var etat = _composition.Acces.Lire(() => _composition.Depot.Obtenir(EntiteSynchro.Element, id));
            if (etat is null) return true; // partie à la corbeille depuis un autre écran

            // Repartir de l'entité STOCKÉE : un objet neuf écraserait ses champs d'audit (§3.1).
            note = Core.Json.SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);
        }
        else
        {
            note = new Element { Type = TypeElement.Note, Statut = StatutElement.Active };
        }

        note.Titre = titre;
        note.Description = Texte;

        var resultat = _composition.Acces.Lire(() =>
            _composition.Saisie.Enregistrer(note, EntiteSynchro.Element));

        if (!resultat.Reussi)
        {
            Etat = resultat.Erreurs.FirstOrDefault()?.Message ?? "Enregistrement refusé.";
            return false;
        }

        Modifiee = false;
        Charger();
        return true;
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
            Vider();
    }

    private static string Apercu(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "Vide";

        var ligne = description.ReplaceLineEndings(" ").Trim();
        return ligne.Length <= 70 ? ligne : ligne[..70].TrimEnd() + "…";
    }
}
