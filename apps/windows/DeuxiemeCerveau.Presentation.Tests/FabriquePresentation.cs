using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Monte un graphe applicatif COMPLET sur un dossier temporaire : vraie base SQLite, vraies
/// migrations du cœur, vrais services. Seuls le réseau et l'identité sont absents
/// (<see cref="FournisseurJetonAbsent"/>) — c'est-à-dire exactement l'état « hors ligne », qui est
/// le mode nominal de l'app (filet 1), pas un cas de repli.
/// </summary>
public sealed class FabriquePresentation : IDisposable
{
    private readonly string _dossier;

    public FabriquePresentation()
    {
        _dossier = Path.Combine(Path.GetTempPath(), "dc-tests-" + Guid.NewGuid().ToString("N"));
        Composition = Composition.Creer(new OptionsApp(), new FournisseurJetonAbsent(), _dossier);
    }

    public Composition Composition { get; }

    /// <summary>Le dossier de données de ce poste — ce qui n'est pas dans la base y vit aussi.</summary>
    public string Dossier => _dossier;

    /// <summary>
    /// La coquille montée sur ce poste. Le sélecteur de fichier renonce toujours : les tests qui
    /// exercent l'export en fournissent un vrai (voir <c>SauvegardeTests</c>), les autres n'y
    /// touchent pas et ne doivent surtout pas ouvrir de boîte de dialogue.
    /// </summary>
    public VueModeleCoquille Coquille() => new(Composition, new SelecteurQuiRenonce());

    private sealed class SelecteurQuiRenonce : ISelecteurFichier
    {
        public Task<Stream?> PourEcrire(string nomPropose) => Task.FromResult<Stream?>(null);
        public Task<Stream?> PourLire() => Task.FromResult<Stream?>(null);
    }

    /// <summary>Enregistre une catégorie (= un calendrier, §3.3) et renvoie son identifiant.</summary>
    public Guid AjouterCategorie(string nom, string couleur = "#4A8C63")
    {
        var categorie = new Categorie { Nom = nom, Couleur = couleur, Origine = OrigineCategorie.Transversale };
        Composition.Saisie.Enregistrer(categorie, EntiteSynchro.Categorie);
        return categorie.Id;
    }

    /// <summary>Enregistre une sortie datée, éventuellement récurrente et catégorisée.</summary>
    public Guid AjouterSortie(
        string titre,
        DateTimeOffset date,
        long centimes = 1_000,
        string? recurrence = null,
        params Guid[] categories)
    {
        var element = new Element
        {
            Type = TypeElement.Facture,
            Titre = titre,
            DateDebut = date,
            Fuseau = "Europe/Paris",
            Recurrence = recurrence,
            MontantCentimes = centimes,
            Devise = "EUR",
            Sens = Sens.Sortie,
            Statut = StatutElement.AVenir,
        };
        foreach (var categorie in categories) element.Categories.Add(categorie);

        Composition.Saisie.Enregistrer(element, EntiteSynchro.Element);
        return element.Id;
    }

    /// <summary>Enregistre un revenu daté, éventuellement récurrent.</summary>
    public Guid AjouterEntree(string titre, DateTimeOffset date, long centimes = 238_000, string? recurrence = null)
    {
        var element = new Element
        {
            Type = TypeElement.Revenu,
            Titre = titre,
            DateDebut = date,
            Fuseau = "Europe/Paris",
            Recurrence = recurrence,
            MontantCentimes = centimes,
            Devise = "EUR",
            Sens = Sens.Entree,
            Statut = StatutElement.Attendu,
        };
        Composition.Saisie.Enregistrer(element, EntiteSynchro.Element);
        return element.Id;
    }

    /// <summary>
    /// Enregistre une envie d'achat (§3.1) — sans date ni montant, comme une liste de souhaits.
    /// <para>
    /// <b>Pas de montant, et ce n'est pas un oubli :</b> le §3.1 réserve l'argent aux types
    /// facture / paiement / revenu, et le cœur le fait respecter (« montant_interdit »). La
    /// maquette montre pourtant des prix sur les envies, et `idees.md` I-003 l'affirme aussi :
    /// c'est une contradiction de la documentation, pas du code. À trancher dans la spec avant
    /// de coder quoi que ce soit qui en dépende.
    /// </para>
    /// </summary>
    public Guid AjouterEnvie(string titre, StatutElement statut = StatutElement.Idee)
    {
        var element = new Element { Type = TypeElement.Envie, Titre = titre, Statut = statut };
        var resultat = Composition.Saisie.Enregistrer(element, EntiteSynchro.Element);

        // Une fabrique de test qui avale un rejet fabrique des tests qui mentent.
        if (!resultat.Reussi)
            throw new InvalidOperationException(
                "Envie refusée : " + string.Join(" / ", resultat.Erreurs.Select(e => e.Message)));

        return element.Id;
    }

    public void Dispose()
    {
        Composition.Dispose();
        try { Directory.Delete(_dossier, recursive: true); } catch { /* dossier temporaire */ }
    }
}
