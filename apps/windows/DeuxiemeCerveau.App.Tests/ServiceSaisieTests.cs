using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Saisie « locale d'abord » (§6.1, filet 1) — Étape 4c. Chaque saisie écrit en local ET pose une
/// entrée d'outbox, sans réseau ; les suppressions sont des marquages (filet 2). Aucun server_seq
/// n'est posé côté client (§6.2).
/// </summary>
public sealed class ServiceSaisieTests : IDisposable
{
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();
    private readonly ServiceSaisie _saisie;

    public ServiceSaisieTests()
    {
        var depot = _base.Depot;
        _saisie = new ServiceSaisie(depot, new IdentiteAppareil(depot), new HorlogeFixe(FabriqueLocale.T0));
    }

    public void Dispose() => _base.Dispose();

    [Fact]
    public void Une_saisie_ecrit_en_local_et_pose_une_entree_d_outbox()
    {
        var facture = FabriqueLocale.NouvelleFacture();
        var resultat = _saisie.Enregistrer(facture, EntiteSynchro.Element);

        Assert.True(resultat.Reussi);
        Assert.NotEqual(Guid.Empty, facture.Id);          // id posé par le service
        Assert.Equal(1, facture.Version);                 // première version
        Assert.Null(facture.ServerSeq);                   // jamais posé côté client (§6.2)

        // Filet 1 : la donnée est en local immédiatement…
        Assert.NotNull(_base.Depot.Obtenir(EntiteSynchro.Element, facture.Id));
        // …et une entrée d'outbox attend l'envoi (persistante).
        var outbox = _base.Depot.Outbox();
        var entree = Assert.Single(outbox);
        Assert.Equal(facture.Id, entree.EntiteId);
        Assert.Equal(1, entree.Version);
    }

    [Fact]
    public void Une_saisie_invalide_n_ecrit_rien_ni_en_local_ni_en_outbox()
    {
        var facture = FabriqueLocale.NouvelleFacture();
        facture.Statut = StatutElement.Fait; // interdit pour une facture (§3.1)

        var resultat = _saisie.Enregistrer(facture, EntiteSynchro.Element);

        Assert.False(resultat.Reussi);
        Assert.NotEmpty(resultat.Erreurs);
        Assert.Empty(_base.Depot.Enumerer(EntiteSynchro.Element)); // rien écrit
        Assert.Empty(_base.Depot.Outbox());                        // rien à envoyer
    }

    [Fact]
    public void Modifier_incremente_la_version_et_ajoute_une_entree_d_outbox()
    {
        var facture = FabriqueLocale.NouvelleFacture(titre: "v1");
        _saisie.Enregistrer(facture, EntiteSynchro.Element);

        facture.Titre = "v2";
        var resultat = _saisie.Enregistrer(facture, EntiteSynchro.Element);

        Assert.True(resultat.Reussi);
        Assert.Equal(2, facture.Version);
        var etat = _base.Depot.Obtenir(EntiteSynchro.Element, facture.Id)!;
        Assert.Equal("v2", SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique).Titre);
        Assert.Single(_base.Depot.Enumerer(EntiteSynchro.Element)); // pas de doublon
        Assert.Equal(2, _base.Depot.Outbox().Count);                // deux modifications en attente
    }

    [Fact]
    public void Supprimer_est_un_marquage_pas_un_effacement_reel()
    {
        var facture = FabriqueLocale.NouvelleFacture();
        _saisie.Enregistrer(facture, EntiteSynchro.Element);

        var resultat = _saisie.Supprimer(EntiteSynchro.Element, facture.Id);

        Assert.True(resultat.Reussi);
        var etat = _base.Depot.Obtenir(EntiteSynchro.Element, facture.Id);
        Assert.NotNull(etat);                 // toujours présent (filet 2 : jamais de DELETE)
        Assert.True(etat.Supprime);           // seulement marqué
        var relu = SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);
        Assert.NotNull(relu.DateSuppression);
        Assert.Equal(2, _base.Depot.Outbox().Count); // création + suppression
    }

    [Fact]
    public void Restaurer_annule_le_marquage_de_suppression()
    {
        var facture = FabriqueLocale.NouvelleFacture();
        _saisie.Enregistrer(facture, EntiteSynchro.Element);
        _saisie.Supprimer(EntiteSynchro.Element, facture.Id);

        var resultat = _saisie.Restaurer(EntiteSynchro.Element, facture.Id);

        Assert.True(resultat.Reussi);
        var etat = _base.Depot.Obtenir(EntiteSynchro.Element, facture.Id)!;
        Assert.False(etat.Supprime);
        Assert.Null(SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique).DateSuppression);
    }

    [Fact]
    public void L_appareil_id_est_stable_entre_les_saisies()
    {
        var f1 = FabriqueLocale.NouvelleFacture(titre: "a");
        var f2 = FabriqueLocale.NouvelleFacture(titre: "b");
        _saisie.Enregistrer(f1, EntiteSynchro.Element);
        _saisie.Enregistrer(f2, EntiteSynchro.Element);

        Assert.Equal(f1.AppareilSource, f2.AppareilSource);
        Assert.NotEqual(Guid.Empty, f1.AppareilSource);
        // Toutes les entrées d'outbox portent le même appareil (§6.2).
        Assert.All(_base.Depot.Outbox(), c => Assert.Equal(f1.AppareilSource, c.AppareilId));
    }

    [Fact]
    public void Chaque_saisie_produit_un_change_id_unique()
    {
        var facture = FabriqueLocale.NouvelleFacture();
        _saisie.Enregistrer(facture, EntiteSynchro.Element);
        _saisie.Enregistrer(facture, EntiteSynchro.Element);

        var ids = _base.Depot.Outbox().Select(c => c.ChangeId).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct().Count()); // idempotence côté serveur : chaque envoi a son change_id
    }
}
