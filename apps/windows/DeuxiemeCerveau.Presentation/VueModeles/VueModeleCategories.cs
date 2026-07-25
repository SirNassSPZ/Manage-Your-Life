using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>Une catégorie en cours d'affichage ou d'édition.</summary>
public sealed partial class LigneCategorie : ObservableObject
{
    public required Guid Id { get; init; }

    /// <summary>Combien d'Éléments s'en servent — pour dire ce qu'une suppression emporte.</summary>
    public required int Usages { get; init; }

    [ObservableProperty]
    private string _nom = "";

    [ObservableProperty]
    private string _couleur = "";

    /// <summary>Vrai quand la ligne est dépliée en formulaire d'édition.</summary>
    [ObservableProperty]
    private bool _enEdition;

    /// <summary>Valeurs de travail, pour qu'un abandon ne laisse aucune trace.</summary>
    [ObservableProperty]
    private string _nomEnCours = "";

    [ObservableProperty]
    private string _couleurEnCours = "";

    public string Resume => Usages switch
    {
        0 => "Aucun élément",
        1 => "1 élément",
        _ => $"{Usages} éléments",
    };
}

/// <summary>
/// Gestion des catégories (§3.3 — catégorie = label = calendrier, une seule notion).
/// <para>
/// Manque comblé : le §3.3 fait de la catégorie un objet de premier plan et le filtre du
/// calendrier (§5.4), mais aucun écran ne permettait d'en créer une. Tout passe par la saisie
/// ordinaire — locale d'abord, outbox, synchro (§6) — et la suppression est un marquage (filet 2),
/// donc récupérable depuis la corbeille.
/// </para>
/// </summary>
public sealed partial class VueModeleCategories : ObservableObject
{
    /// <summary>
    /// La palette « dustée » de la maquette. Fermée volontairement : un sélecteur de couleur libre
    /// laisserait l'utilisateur casser l'harmonie de ses propres calendriers.
    /// </summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#BB5A44", // terre cuite — sorties
        "#4A8C63", // sauge — revenus
        "#4B7F8A", // ardoise — travail
        "#A97E3C", // ocre — santé
        "#7A6AA6", // mauve — perso
    ];

    private readonly Composition _composition;

    public VueModeleCategories(Composition composition)
    {
        _composition = composition;
        NouvelleCouleur = Palette[0];
        Charger();
    }

    /// <summary>
    /// Prévient la coquille qu'un calendrier a changé : sa barre latérale liste les mêmes
    /// catégories, elle ne doit pas rester sur une version périmée.
    /// </summary>
    public Action? ApresChangement { get; set; }

    public ObservableCollection<LigneCategorie> Categories { get; } = [];

    [ObservableProperty]
    private string _nouveauNom = "";

    [ObservableProperty]
    private string _nouvelleCouleur = "";

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _aucuneCategorie;

    [RelayCommand]
    private void ChoisirCouleur(string couleur) => NouvelleCouleur = couleur;

    [RelayCommand]
    private void Ajouter()
    {
        var nom = NouveauNom.Trim();
        if (nom.Length == 0)
        {
            Message = "Donne un nom à ta catégorie.";
            return;
        }

        // Deux calendriers homonymes sont indiscernables dans la barre latérale : on refuse tôt,
        // plutôt que de laisser l'utilisateur s'y perdre.
        if (Categories.Any(c => string.Equals(c.Nom, nom, StringComparison.CurrentCultureIgnoreCase)))
        {
            Message = $"« {nom} » existe déjà.";
            return;
        }

        var resultat = _composition.Acces.Lire(() => _composition.Saisie.Enregistrer(
            new Categorie { Nom = nom, Couleur = NouvelleCouleur, Origine = OrigineCategorie.Transversale },
            EntiteSynchro.Categorie));

        if (!resultat.Reussi)
        {
            Message = resultat.Erreurs.FirstOrDefault()?.Message ?? "Création refusée.";
            return;
        }

        NouveauNom = "";
        Message = null;
        Recharger();
    }

    [RelayCommand]
    private void Modifier(LigneCategorie ligne)
    {
        foreach (var autre in Categories) autre.EnEdition = false;
        ligne.NomEnCours = ligne.Nom;
        ligne.CouleurEnCours = ligne.Couleur;
        ligne.EnEdition = true;
        Message = null;
    }

    [RelayCommand]
    private void ChoisirCouleurEnCours((LigneCategorie Ligne, string Couleur) choix) =>
        choix.Ligne.CouleurEnCours = choix.Couleur;

    [RelayCommand]
    private void Annuler(LigneCategorie ligne) => ligne.EnEdition = false;

    [RelayCommand]
    private void Enregistrer(LigneCategorie ligne)
    {
        var nom = ligne.NomEnCours.Trim();
        if (nom.Length == 0)
        {
            Message = "Le nom ne peut pas être vide.";
            return;
        }

        if (Categories.Any(c => c.Id != ligne.Id
            && string.Equals(c.Nom, nom, StringComparison.CurrentCultureIgnoreCase)))
        {
            Message = $"« {nom} » existe déjà.";
            return;
        }

        var etat = _composition.Acces.Lire(() =>
            _composition.Depot.Obtenir(EntiteSynchro.Categorie, ligne.Id));
        if (etat is null) { Message = "Catégorie introuvable."; return; }

        // On repart de l'entité STOCKÉE : reconstruire un objet neuf écraserait ses champs
        // d'audit (date de création, version) — §3.1.
        var categorie = Core.Json.SerialisationCanonique.Deserialiser<Categorie>(etat.PayloadCanonique);
        categorie.Nom = nom;
        categorie.Couleur = ligne.CouleurEnCours;

        var resultat = _composition.Acces.Lire(() =>
            _composition.Saisie.Enregistrer(categorie, EntiteSynchro.Categorie));

        Message = resultat.Reussi ? null : resultat.Erreurs.FirstOrDefault()?.Message;
        Recharger();
    }

    /// <summary>
    /// Met la catégorie à la corbeille (filet 2 : marquage, jamais destruction). Les Éléments qui
    /// la portaient gardent la référence — une restauration les retrouve tels quels.
    /// </summary>
    [RelayCommand]
    private void Supprimer(LigneCategorie ligne)
    {
        var resultat = _composition.Acces.Lire(() =>
            _composition.Saisie.Supprimer(EntiteSynchro.Categorie, ligne.Id));

        Message = resultat.Reussi
            ? ligne.Usages == 0
                ? $"« {ligne.Nom} » est à la corbeille."
                : $"« {ligne.Nom} » est à la corbeille — {ligne.Resume.ToLowerInvariant()} la portai(en)t encore."
            : resultat.Erreurs.FirstOrDefault()?.Message;

        Recharger();
    }

    private void Recharger()
    {
        Charger();
        ApresChangement?.Invoke();
    }

    public void Charger()
    {
        var (categories, elements) = _composition.Acces.Lire(() =>
            (_composition.Lecture.Categories(), _composition.Lecture.Actifs()));

        // Un seul parcours des Éléments : compter par catégorie coûte moins qu'un filtre par ligne.
        var usages = new Dictionary<Guid, int>();
        foreach (var element in elements)
            foreach (var categorie in element.Categories)
                usages[categorie] = usages.GetValueOrDefault(categorie) + 1;

        Categories.Clear();
        foreach (var categorie in categories)
            Categories.Add(new LigneCategorie
            {
                Id = categorie.Id,
                Nom = categorie.Nom,
                Couleur = string.IsNullOrWhiteSpace(categorie.Couleur) ? Palette[^1] : categorie.Couleur,
                Usages = usages.GetValueOrDefault(categorie.Id),
            });

        AucuneCategorie = Categories.Count == 0;
    }
}
