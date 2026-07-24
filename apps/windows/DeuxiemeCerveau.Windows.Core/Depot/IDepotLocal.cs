using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Windows.Core.Synchro;

namespace DeuxiemeCerveau.Windows.Core.Depot;

/// <summary>
/// Interface d'accès au magasin local SQLite (local-first, §6).
/// Garantit l'écriture locale immédiate et l'insertion automatique en Outbox (Filet 1 & 2).
/// </summary>
public interface IDepotLocal : IDisposable
{
    // ----- Configuration & Appareil -----
    Guid AppareilId { get; }
    void DefinirAppareilId(Guid id);
    long ObtenirCurseurPull();
    void SauvegarderCurseurPull(long curseur);

    // ----- Éléments -----
    Element? ObtenirElement(Guid id);
    IReadOnlyList<Element> ListerElements(bool inclureSupprimes = false);
    void EnregistrerElement(Element element);
    void SupprimerElement(Guid id);
    void RestaurerElement(Guid id);

    // ----- Catégories -----
    Categorie? ObtenirCategorie(Guid id);
    IReadOnlyList<Categorie> ListerCategories(bool inclureSupprimes = false);
    void EnregistrerCategorie(Categorie categorie);

    // ----- Projets -----
    Projet? ObtenirProjet(Guid id);
    IReadOnlyList<Projet> ListerProjets(bool inclureSupprimes = false);
    void EnregistrerProjet(Projet projet);

    // ----- Budgets -----
    Budget? ObtenirBudget(Guid id);
    IReadOnlyList<Budget> ListerBudgets(bool inclureSupprimes = false);
    void EnregistrerBudget(Budget budget);

    // ----- Solde de référence (§3.4) -----
    ReglageSolde? ObtenirSoldeReference();
    void RecalerSoldeReference(ReglageSolde reglage, Guid changeId);

    // ----- Outbox & Synchro Client -----
    IReadOnlyList<EntreeOutbox> ObtenirOutbox();
    void NettoyerOutbox(IEnumerable<Guid> changeIdsConfirmes);
    void AppliquerPull(PagePull page);
    void PurgerLocalement(EntiteSynchro entite, Guid id);
}
