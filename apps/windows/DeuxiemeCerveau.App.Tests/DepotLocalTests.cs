using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Migrations;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Dépôt local (base SQLite montée par les vraies migrations §9, dialecte local) — Étape 4b. Prouve
/// la fidélité aller-retour du payload canonique, la persistance de l'outbox (§6.2) et du curseur.
/// </summary>
public sealed class DepotLocalTests : IDisposable
{
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();

    public void Dispose() => _base.Dispose();

    [Fact]
    public void Les_migrations_du_coeur_sont_appliquees_a_l_ouverture()
        => Assert.Equal(ListeMigrations.Toutes.Count, _base.Migrations.Count);

    [Fact]
    public void Ecrire_puis_obtenir_un_element_conserve_le_payload()
    {
        var facture = FabriqueLocale.Facture(titre: "Loyer", montant: 80000);
        facture.Description = "avec accents : éàü";
        _base.Depot.Ecrire(FabriqueLocale.Etat(facture, EntiteSynchro.Element));

        var etat = _base.Depot.Obtenir(EntiteSynchro.Element, facture.Id);
        Assert.NotNull(etat);
        var relu = SerialisationCanonique.Deserialiser<Element>(etat.PayloadCanonique);
        Assert.Equal("Loyer", relu.Titre);
        Assert.Equal("avec accents : éàü", relu.Description);
        Assert.Equal(80000, relu.MontantCentimes);
        Assert.Equal("Europe/Paris", relu.Fuseau);
    }

    [Fact]
    public void Ecrire_deux_fois_le_meme_id_met_a_jour_sans_doublon()
    {
        var facture = FabriqueLocale.Facture(titre: "v1");
        _base.Depot.Ecrire(FabriqueLocale.Etat(facture, EntiteSynchro.Element));

        facture.Titre = "v2";
        facture.Version = 2;
        _base.Depot.Ecrire(FabriqueLocale.Etat(facture, EntiteSynchro.Element));

        var tous = _base.Depot.Enumerer(EntiteSynchro.Element);
        Assert.Single(tous);
        Assert.Equal("v2", SerialisationCanonique.Deserialiser<Element>(tous[0].PayloadCanonique).Titre);
    }

    [Fact]
    public void Enumerer_rend_la_corbeille_comprise()
    {
        _base.Depot.Ecrire(FabriqueLocale.Etat(FabriqueLocale.Facture(titre: "vivant"), EntiteSynchro.Element));
        _base.Depot.Ecrire(FabriqueLocale.Etat(FabriqueLocale.Facture(titre: "corbeille", supprime: true), EntiteSynchro.Element));

        var tous = _base.Depot.Enumerer(EntiteSynchro.Element);
        Assert.Equal(2, tous.Count);
        Assert.Contains(tous, e => e.Supprime);
    }

    [Fact]
    public void Le_reglage_de_solde_fait_l_aller_retour()
    {
        _base.Depot.Ecrire(FabriqueLocale.Etat(FabriqueLocale.Reglage(150000), EntiteSynchro.Reglage));

        var etat = _base.Depot.Obtenir(EntiteSynchro.Reglage, ReglageSolde.IdSoldeReference);
        Assert.NotNull(etat);
        var relu = SerialisationCanonique.Deserialiser<ReglageSolde>(etat.PayloadCanonique);
        Assert.Equal(150000, relu.SoldeReferenceCentimes);
        Assert.Equal(new DateOnly(2026, 7, 1), relu.SoldeReferenceDate);
    }

    [Fact]
    public void L_outbox_persiste_et_s_ordonne_puis_se_vide()
    {
        var f1 = FabriqueLocale.Facture(titre: "premier");
        var f2 = FabriqueLocale.Facture(titre: "second");
        var c1 = FabriqueLocale.Changement(f1, EntiteSynchro.Element);
        var c2 = FabriqueLocale.Changement(f2, EntiteSynchro.Element);
        _base.Depot.AjouterOutbox(c1);
        _base.Depot.AjouterOutbox(c2);

        var attente = _base.Depot.Outbox();
        Assert.Equal(2, attente.Count);
        Assert.Equal(c1.ChangeId, attente[0].ChangeId); // ordre de création préservé (lots ordonnés §6.2)
        Assert.Equal(c2.ChangeId, attente[1].ChangeId);

        _base.Depot.RetirerOutbox(c1.ChangeId); // changement confirmé par le serveur (§6.2, étape 5)
        var reste = _base.Depot.Outbox();
        Assert.Single(reste);
        Assert.Equal(c2.ChangeId, reste[0].ChangeId);
    }

    [Fact]
    public void Vider_l_outbox_d_une_entite_abandonne_ses_changements_en_attente()
    {
        var facture = FabriqueLocale.Facture();
        _base.Depot.AjouterOutbox(FabriqueLocale.Changement(facture, EntiteSynchro.Element));
        _base.Depot.AjouterOutbox(FabriqueLocale.Changement(FabriqueLocale.Facture(), EntiteSynchro.Element));

        _base.Depot.ViderOutboxEntite(EntiteSynchro.Element, facture.Id);

        var reste = _base.Depot.Outbox();
        Assert.Single(reste);
        Assert.DoesNotContain(reste, c => c.EntiteId == facture.Id);
    }

    [Fact]
    public void Le_curseur_de_pull_se_lit_et_s_ecrit()
    {
        Assert.Null(_base.Depot.LireEtat("curseur_pull"));
        _base.Depot.EcrireEtat("curseur_pull", "42");
        Assert.Equal("42", _base.Depot.LireEtat("curseur_pull"));
        _base.Depot.EcrireEtat("curseur_pull", "99"); // remplacement
        Assert.Equal("99", _base.Depot.LireEtat("curseur_pull"));
    }

    [Fact]
    public void Supprimer_reel_detruit_localement_la_purge_manuelle()
    {
        var facture = FabriqueLocale.Facture(supprime: true);
        _base.Depot.Ecrire(FabriqueLocale.Etat(facture, EntiteSynchro.Element));
        Assert.NotNull(_base.Depot.Obtenir(EntiteSynchro.Element, facture.Id));

        _base.Depot.SupprimerReel(EntiteSynchro.Element, facture.Id); // purge depuis la corbeille (§5.6)
        Assert.Null(_base.Depot.Obtenir(EntiteSynchro.Element, facture.Id));
    }
}
