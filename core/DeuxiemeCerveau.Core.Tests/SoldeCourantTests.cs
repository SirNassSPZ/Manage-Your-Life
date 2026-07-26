using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Projection;
using Xunit;

namespace DeuxiemeCerveau.Core.Tests;

/// <summary>
/// Le <b>solde courant</b> (§5.1) — le chiffre que l'utilisateur lit en premier.
/// <para>
/// Il existe parce que l'accueil affichait le <b>solde de référence</b>, immobile par conception
/// (§3.4), en le faisant passer pour l'argent du moment : « 700 € bloqué, je ne sais pas ce que ça
/// représente ». Le solde courant, lui, découle de ce qui est encodé.
/// </para>
/// </summary>
public class SoldeCourantTests
{
    private static readonly DateOnly DateReference = new(2026, 7, 1);
    private static readonly SoldeReference Reference = new(100_000, DateReference);

    private static DateTimeOffset Le(int jour, int heure = 12) =>
        new(2026, 7, jour, heure, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sans_rien_d_encode_le_solde_courant_est_la_reference()
    {
        Assert.Equal(100_000, CalculateurProjection.SoldeCourant(Reference, [], Le(15)));
    }

    [Fact]
    public void Une_reference_a_zero_et_rien_d_encode_donne_zero()
    {
        // Demande explicite : « si on n'a rien à encoder il faut que le montant soit à 0 ».
        Assert.Equal(0, CalculateurProjection.SoldeCourant(new SoldeReference(0, DateReference), [], Le(15)));
    }

    [Fact]
    public void Une_sortie_deja_passee_est_deduite()
    {
        var loyer = Fabrique.Facture(montant: 80_000, dateDebut: Le(5));

        Assert.Equal(20_000, CalculateurProjection.SoldeCourant(Reference, [loyer], Le(15)));
    }

    [Fact]
    public void Une_entree_deja_passee_est_ajoutee()
    {
        var salaire = Fabrique.Revenu(montant: 220_000, dateDebut: Le(5));

        Assert.Equal(320_000, CalculateurProjection.SoldeCourant(Reference, [salaire], Le(15)));
    }

    [Fact]
    public void Ce_qui_n_est_pas_encore_echu_ne_compte_pas_encore()
    {
        // C'est ce qui distingue le solde courant de la clôture du mois : il s'arrête à MAINTENANT.
        var loyer = Fabrique.Facture(montant: 80_000, dateDebut: Le(25));

        Assert.Equal(100_000, CalculateurProjection.SoldeCourant(Reference, [loyer], Le(15)));
        Assert.Equal(20_000, CalculateurProjection.SoldeCourant(Reference, [loyer], Le(26)));
    }

    [Fact]
    public void Le_chiffre_bouge_a_chaque_saisie()
    {
        // La demande, en une phrase : « le montant doit être exactement proportionné par rapport
        // aux entrées sorties ».
        var elements = new List<Element>();
        Assert.Equal(100_000, CalculateurProjection.SoldeCourant(Reference, elements, Le(20)));

        elements.Add(Fabrique.Facture(montant: 30_000, dateDebut: Le(5)));
        Assert.Equal(70_000, CalculateurProjection.SoldeCourant(Reference, elements, Le(20)));

        elements.Add(Fabrique.Revenu(montant: 50_000, dateDebut: Le(6)));
        Assert.Equal(120_000, CalculateurProjection.SoldeCourant(Reference, elements, Le(20)));
    }

    [Fact]
    public void Une_envie_ne_deplace_jamais_le_solde_courant()
    {
        // GARDE-FOU — à ne pas supprimer en croyant à un doublon (D-027). Depuis que l'envie peut
        // porter un prix, ce n'est plus l'absence du champ qui protège le calcul mais son exclusion
        // stricte : une envie n'est pas financière, donc elle n'entre pas. « Ne rien inventer sur
        // les envies d'achat » se vérifie ici.
        var envie = Fabrique.Envie("Casque audio");
        envie.MontantCentimes = 30_000;
        envie.Devise = "EUR";

        Assert.Equal(100_000, CalculateurProjection.SoldeCourant(Reference, [envie], Le(20)));
    }

    [Fact]
    public void Les_exclusions_sont_les_memes_que_la_projection()
    {
        // Mêmes règles que §5.1 — le solde courant n'est pas un second algorithme.
        var annulee = Fabrique.Facture(montant: 40_000, dateDebut: Le(5), statut: StatutElement.Annule);

        var corbeille = Fabrique.Facture(montant: 40_000, dateDebut: Le(5));
        corbeille.Supprime = true;
        corbeille.DateSuppression = Le(6);

        var sansDate = Fabrique.Facture(montant: 40_000);
        sansDate.DateDebut = null;
        sansDate.Fuseau = null;

        var note = Fabrique.Note();

        Assert.Equal(100_000,
            CalculateurProjection.SoldeCourant(Reference, [annulee, corbeille, sansDate, note], Le(20)));
    }

    [Fact]
    public void Un_paiement_deja_marque_paye_compte_quand_meme()
    {
        // §5.1.3 : les occurrences déjà payées sont incluses — le solde de référence, antérieur, ne
        // les contient pas encore. Les exclure les compterait deux fois en sens inverse.
        var payee = Fabrique.Facture(montant: 25_000, dateDebut: Le(5), statut: StatutElement.Paye);

        Assert.Equal(75_000, CalculateurProjection.SoldeCourant(Reference, [payee], Le(20)));
    }

    [Fact]
    public void Ce_qui_precede_la_reference_est_deja_dedans()
    {
        // §5.1.3 : le solde de référence contient déjà tout ce qui l'a précédé. Le recompter le
        // ferait compter deux fois.
        var avant = Fabrique.Facture(montant: 50_000, dateDebut: new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(100_000, CalculateurProjection.SoldeCourant(Reference, [avant], Le(20)));
    }

    [Fact]
    public void Une_recurrence_compte_autant_de_fois_qu_elle_est_echue()
    {
        // Le loyer du 5, mensuel : au 20 septembre il est passé trois fois (juillet, août, septembre).
        var loyer = Fabrique.Facture(montant: 80_000, dateDebut: Le(5), recurrence: "FREQ=MONTHLY");

        var auVingtJuillet = CalculateurProjection.SoldeCourant(Reference, [loyer], Le(20));
        var auVingtSeptembre = CalculateurProjection.SoldeCourant(
            Reference, [loyer], new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(20_000, auVingtJuillet);          // 100 000 − 80 000
        Assert.Equal(-140_000, auVingtSeptembre);      // 100 000 − 3 × 80 000
    }

    [Fact]
    public void Il_rejoint_la_cloture_du_mois_quand_on_se_place_a_la_fin_du_mois()
    {
        // La preuve qu'il n'y a qu'une arithmétique : au dernier instant du mois, le solde courant
        // et la clôture projetée du mois doivent tomber sur le même centime.
        Element[] elements =
        [
            Fabrique.Facture(montant: 80_000, dateDebut: Le(5)),
            Fabrique.Revenu(montant: 220_000, dateDebut: Le(28)),
        ];

        var finJuillet = new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero);
        var courant = CalculateurProjection.SoldeCourant(Reference, elements, finJuillet);

        var projection = CalculateurProjection.Calculer(new RequeteProjection(
            new MoisCalendaire(2026, 7), 1, Reference, elements));

        Assert.Equal(projection[0].ClotureCentimes, courant);
    }
}
