using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Services;

/// <summary>
/// Un modèle de départ (D-018 #1) : une charge ou un revenu courant pré-rempli, proposé à l'onboarding
/// pour éviter la page blanche. Ce n'est QUE du contenu — il produit un Élément standard (§3.1), sans
/// aucun champ ni entité nouvelle. Réutilisé à l'identique par l'app Apple (même liste, depuis la spec).
/// </summary>
public sealed record ModeleDepart(string Cle, string Titre, TypeElement Type, Sens Sens, string Recurrence)
{
    /// <summary>
    /// Compose un Élément « frais » (sans id ni audit — la saisie les pose) depuis ce modèle et les
    /// valeurs saisies par l'utilisateur (montant, première échéance, fuseau).
    /// </summary>
    public Element Composer(long montantCentimes, DateTimeOffset premiereEcheance, string fuseau = "Europe/Paris")
        => new()
        {
            Type = Type,
            Titre = Titre,
            DateDebut = premiereEcheance,
            Fuseau = fuseau,
            Recurrence = Recurrence,
            MontantCentimes = montantCentimes,
            Devise = "EUR",
            Sens = Sens,
            Statut = Sens == Sens.Entree ? StatutElement.Attendu : StatutElement.AVenir,
        };
}

/// <summary>Catalogue des modèles de départ (D-018 #1) — les entrées/sorties récurrentes les plus courantes.</summary>
public static class CatalogueDepart
{
    public static IReadOnlyList<ModeleDepart> Modeles { get; } =
    [
        new("salaire",     "Salaire",     TypeElement.Revenu,  Sens.Entree, "FREQ=MONTHLY"),
        new("loyer",       "Loyer",       TypeElement.Facture, Sens.Sortie, "FREQ=MONTHLY"),
        new("electricite", "Électricité", TypeElement.Facture, Sens.Sortie, "FREQ=MONTHLY"),
        new("internet",    "Internet",    TypeElement.Facture, Sens.Sortie, "FREQ=MONTHLY"),
        new("mobile",      "Téléphone",   TypeElement.Facture, Sens.Sortie, "FREQ=MONTHLY"),
        new("abonnements", "Abonnements", TypeElement.Facture, Sens.Sortie, "FREQ=MONTHLY"),
        new("assurance",   "Assurance",   TypeElement.Facture, Sens.Sortie, "FREQ=MONTHLY"),
    ];
}

/// <summary>
/// Démarrage / premier lancement (feuille de route Palier 1, rang 2 / reco #1) : amener l'utilisateur en
/// 3 gestes jusqu'à une projection — (1) poser le solde de référence, (2) 1-2 revenus, (3) ses charges
/// fixes depuis les modèles de départ.
///
/// <para>Tout passe par la <b>saisie ordinaire</b> (locale d'abord, outbox, synchro — §6) : aucune route
/// ni logique parallèle. Poser le solde de référence, c'est enregistrer le réglage `solde_reference`
/// (§3.4, D-006) — donc le <b>recalage</b> (reco #9) réutilise exactement le même geste.</para>
/// </summary>
public sealed class ServiceDemarrage(DepotLocal depot, ServiceSaisie saisie)
{
    /// <summary>Vrai tant que le solde de référence (§3.4) n'a jamais été posé — signal « montrer l'onboarding ».</summary>
    public bool OnboardingRequis()
        => !depot.Enumerer(EntiteSynchro.Reglage).Any(e => e.Id == ReglageSolde.IdSoldeReference && !e.Supprime);

    /// <summary>
    /// Geste 1 (et recalage, reco #9) : poser le solde de référence. Passe par la saisie synchronisée
    /// (dernier recalage gagne, §3.4) ; l'identifiant déterministe garantit qu'il n'y en a qu'un.
    /// </summary>
    public ResultatSaisie DefinirSoldeReference(long centimes, DateOnly date)
    {
        // Recalage = mise à jour du réglage existant : on REPREND sa date de création (champ d'audit qui
        // ne doit jamais changer, §3.1) au lieu de repartir d'un objet vierge.
        var existant = depot.Obtenir(EntiteSynchro.Reglage, ReglageSolde.IdSoldeReference);
        var reglage = existant is null
            ? new ReglageSolde { Id = ReglageSolde.IdSoldeReference }
            : SerialisationCanonique.Deserialiser<ReglageSolde>(existant.PayloadCanonique);

        reglage.SoldeReferenceCentimes = centimes;
        reglage.SoldeReferenceDate = date;
        reglage.Supprime = false; // un recalage réactive le réglage s'il avait été mis à la corbeille
        return saisie.Enregistrer(reglage, EntiteSynchro.Reglage);
    }

    /// <summary>Les modèles de départ proposés (D-018 #1), pour la 3ᵉ étape de l'onboarding.</summary>
    public IReadOnlyList<ModeleDepart> Modeles => CatalogueDepart.Modeles;
}
