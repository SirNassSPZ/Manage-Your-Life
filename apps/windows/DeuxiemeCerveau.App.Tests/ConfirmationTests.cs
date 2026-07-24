using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Confirmation « payé / reçu » + sélection multiple (feuille de route Palier 1 rang 4 / reco #7,
/// décision D-017 Option A) — Étape 4f. On vérifie la transition de statut sur les Éléments PONCTUELS et
/// le refus explicite des récurrents (leur correction = recalage, §3.4).
/// </summary>
public sealed class ConfirmationTests : IDisposable
{
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();
    private readonly ServiceSaisie _saisie;
    private readonly ServiceConfirmation _confirmation;
    private readonly ServiceLecture _lecture;

    public ConfirmationTests()
    {
        var id = new IdentiteAppareil(_base.Depot);
        _saisie = new ServiceSaisie(_base.Depot, id, new HorlogeFixe(FabriqueLocale.T0));
        _confirmation = new ServiceConfirmation(_base.Depot, _saisie);
        _lecture = new ServiceLecture(_base.Depot);
    }

    public void Dispose() => _base.Dispose();

    private static Element Revenu(string titre = "Salaire", StatutElement statut = StatutElement.Attendu)
        => new()
        {
            Type = TypeElement.Revenu, Titre = titre, Sens = Sens.Entree, MontantCentimes = 240000,
            Devise = "EUR", DateDebut = new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero),
            Fuseau = "Europe/Paris", Statut = statut,
        };

    private StatutElement StatutDe(Guid id) => _lecture.Actifs().Single(e => e.Id == id).Statut;

    [Fact]
    public void Confirmer_une_facture_ponctuelle_la_passe_a_paye()
    {
        var facture = FabriqueLocale.NouvelleFacture(titre: "Électricité"); // ponctuelle, a_venir
        _saisie.Enregistrer(facture, EntiteSynchro.Element);

        Assert.True(_confirmation.Confirmer(facture.Id).Reussi);
        Assert.Equal(StatutElement.Paye, StatutDe(facture.Id));
    }

    [Fact]
    public void Confirmer_un_revenu_ponctuel_le_passe_a_recu()
    {
        var revenu = Revenu();
        _saisie.Enregistrer(revenu, EntiteSynchro.Element);

        Assert.True(_confirmation.Confirmer(revenu.Id).Reussi);
        Assert.Equal(StatutElement.Recu, StatutDe(revenu.Id));
    }

    [Fact]
    public void Confirmer_une_recurrence_est_refuse_avec_renvoi_au_recalage()
    {
        var loyer = FabriqueLocale.NouvelleFacture(titre: "Loyer", recurrence: "FREQ=MONTHLY");
        _saisie.Enregistrer(loyer, EntiteSynchro.Element);

        var r = _confirmation.Confirmer(loyer.Id);

        Assert.False(r.Reussi);
        Assert.Equal("recurrent_non_confirmable", r.Erreurs[0].Code);
        Assert.Equal(StatutElement.AVenir, StatutDe(loyer.Id)); // inchangé
    }

    [Fact]
    public void Confirmer_deux_fois_ou_mauvais_statut_est_refuse()
    {
        var facture = FabriqueLocale.NouvelleFacture(titre: "Assurance");
        _saisie.Enregistrer(facture, EntiteSynchro.Element);
        Assert.True(_confirmation.Confirmer(facture.Id).Reussi); // a_venir → paye

        var deuxieme = _confirmation.Confirmer(facture.Id);      // déjà payé → plus rien à confirmer
        Assert.False(deuxieme.Reussi);
        Assert.Equal("non_confirmable", deuxieme.Erreurs[0].Code);
    }

    [Fact]
    public void Confirmer_en_lot_traite_chaque_element_independamment()
    {
        var f1 = FabriqueLocale.NouvelleFacture(titre: "Internet");
        var f2 = FabriqueLocale.NouvelleFacture(titre: "Courses");
        var recurrent = FabriqueLocale.NouvelleFacture(titre: "Loyer", recurrence: "FREQ=MONTHLY");
        foreach (var e in new[] { f1, f2, recurrent })
            _saisie.Enregistrer(e, EntiteSynchro.Element);

        var bilan = _confirmation.ConfirmerLot([f1.Id, f2.Id, recurrent.Id]);

        Assert.Equal(2, bilan.Confirmes.Count);                 // les deux ponctuelles
        Assert.Contains(f1.Id, bilan.Confirmes);
        Assert.Contains(f2.Id, bilan.Confirmes);
        Assert.Single(bilan.Refuses);                           // la récurrente refusée
        Assert.Equal(recurrent.Id, bilan.Refuses[0].Id);
        Assert.Equal(StatutElement.Paye, StatutDe(f1.Id));
    }

    [Fact]
    public void AConfirmer_liste_les_ponctuels_dus_et_exclut_recurrents_et_futurs()
    {
        var echu = FabriqueLocale.NouvelleFacture(titre: "Électricité"); // 5 juillet, ponctuelle
        var futur = FabriqueLocale.NouvelleFacture(titre: "Plus tard");
        futur.DateDebut = new DateTimeOffset(2026, 8, 20, 7, 0, 0, TimeSpan.Zero);
        var recurrent = FabriqueLocale.NouvelleFacture(titre: "Loyer", recurrence: "FREQ=MONTHLY");
        foreach (var e in new[] { echu, futur, recurrent })
            _saisie.Enregistrer(e, EntiteSynchro.Element);

        var dus = _confirmation.AConfirmer(echusAu: new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero));

        var seul = Assert.Single(dus);
        Assert.Equal("Électricité", seul.Titre); // le futur (20 août) et la récurrente sont exclus
    }
}
