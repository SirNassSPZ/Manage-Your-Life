using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Projection;
using Xunit;

namespace DeuxiemeCerveau.Core.Tests;

/// <summary>
/// Confrontation d'une envie au budget projeté (§5.1bis, entrée en V1 par D-027).
/// « Est-ce que ça rentre en septembre ? »
/// </summary>
public class ConfrontationTests
{
    private static readonly MoisCalendaire Janvier = new(2026, 1);

    /// <summary>Un poste simple : 1 000 € au départ, aucun mouvement — la cascade est plate.</summary>
    private static RequeteProjection Base(long soldeCentimes, params Element[] elements) => new(
        PremierMois: Janvier,
        NombreMois: 6,
        Solde: new SoldeReference(soldeCentimes, new DateOnly(2026, 1, 1)),
        Elements: elements);

    private static Element Sortie(string titre, DateOnly date, long centimes) => new()
    {
        Id = Guid.NewGuid(),
        Type = TypeElement.Facture,
        Titre = titre,
        DateDebut = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
        Fuseau = "Europe/Paris",
        MontantCentimes = centimes,
        Devise = "EUR",
        Sens = Sens.Sortie,
        Statut = StatutElement.AVenir,
    };

    [Fact]
    public void Ce_qui_rentre_passe_et_laisse_le_reste_visible()
    {
        var confrontation = CalculateurProjection.Confronter(
            new RequeteConfrontation(Base(100_000), MontantCentimes: 30_000, MoisCible: new(2026, 3)));

        Assert.True(confrontation.Passe);
        Assert.Null(confrontation.PremierMoisQuiCasse);
        Assert.Equal(0, confrontation.ManqueCentimes);

        // Mars ampute de 300 € et le report suit : c'est la cascade §5.1, inchangée.
        Assert.Equal(100_000, confrontation.Nominale[2].ClotureCentimes);
        Assert.Equal(70_000, confrontation.Simulee[2].ClotureCentimes);
        Assert.Equal(70_000, confrontation.Simulee[5].ClotureCentimes);
    }

    [Fact]
    public void Ce_qui_ne_rentre_pas_dit_quand_et_de_combien()
    {
        var confrontation = CalculateurProjection.Confronter(
            new RequeteConfrontation(Base(20_000), MontantCentimes: 35_000, MoisCible: new(2026, 2)));

        Assert.False(confrontation.Passe);
        Assert.NotNull(confrontation.PremierMoisQuiCasse);
        Assert.Equal(2, confrontation.PremierMoisQuiCasse!.Mois);
        Assert.Equal(15_000, confrontation.ManqueCentimes);   // 200 € − 350 € = −150 €
    }

    [Fact]
    public void La_projection_nominale_est_rendue_intacte()
    {
        // C'est ce qui permet à l'app de montrer l'écart, et non un simple oui/non.
        var requete = Base(100_000, Sortie("Loyer", new DateOnly(2026, 2, 5), 84_000));

        var seule = CalculateurProjection.Calculer(requete);
        var confrontation = CalculateurProjection.Confronter(
            new RequeteConfrontation(requete, 10_000, new MoisCalendaire(2026, 4)));

        Assert.Equal(seule.Select(m => m.ClotureCentimes),
                     confrontation.Nominale.Select(m => m.ClotureCentimes));
    }

    [Fact]
    public void Un_mois_deja_negatif_AVANT_la_cible_n_est_pas_imputable_a_l_achat()
    {
        // Le piège : sans la borne « à partir de la cible », une dépense parfaitement finançable
        // serait refusée à cause d'un découvert antérieur qu'elle ne provoque pas.
        var requete = Base(10_000, Sortie("Gros imprévu", new DateOnly(2026, 1, 10), 50_000));

        var confrontation = CalculateurProjection.Confronter(
            new RequeteConfrontation(requete, 1_000, new MoisCalendaire(2026, 5)));

        Assert.True(confrontation.Nominale[0].Decouvert);   // janvier est bien dans le rouge
        Assert.False(confrontation.Passe);                  // …mais le report le traîne jusqu'en mai
        Assert.Equal(5, confrontation.PremierMoisQuiCasse!.Mois);
    }

    [Fact]
    public void Une_confrontation_n_ecrit_rien_et_se_rejoue_a_l_identique()
    {
        // Règle 9 : c'est une lecture. Deux appels rendent le même résultat, et la liste
        // d'Éléments fournie n'est pas touchée.
        var elements = new[] { Sortie("Loyer", new DateOnly(2026, 2, 5), 84_000) };
        var requete = Base(200_000, elements);

        var premiere = CalculateurProjection.Confronter(new RequeteConfrontation(requete, 50_000, new(2026, 3)));
        var seconde = CalculateurProjection.Confronter(new RequeteConfrontation(requete, 50_000, new(2026, 3)));

        Assert.Equal(premiere.Passe, seconde.Passe);
        Assert.Equal(premiere.Simulee.Select(m => m.ClotureCentimes),
                     seconde.Simulee.Select(m => m.ClotureCentimes));
        Assert.Single(elements);
        Assert.Equal(84_000, elements[0].MontantCentimes);
    }

    [Fact]
    public void Une_envie_avec_un_prix_n_entre_JAMAIS_dans_la_projection_nominale()
    {
        // LE garde-fou de D-027. Avant, c'était l'interdiction du champ ; maintenant c'est
        // l'exclusion du calcul — donc c'est ici que ça se défend, explicitement.
        var envie = new Element
        {
            Id = Guid.NewGuid(),
            Type = TypeElement.Envie,
            Titre = "Casque audio",
            MontantCentimes = 18_000,
            Devise = "EUR",
            Statut = StatutElement.Idee,
            DateDebut = new DateTimeOffset(new DateTime(2026, 2, 10, 12, 0, 0), TimeSpan.Zero),
            Fuseau = "Europe/Paris",
        };

        var projection = CalculateurProjection.Calculer(Base(100_000, envie));

        Assert.All(projection, m => Assert.Equal(0, m.SortiesCentimes));
        Assert.Equal(100_000, projection[5].ClotureCentimes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_montant_non_positif_est_refuse(long centimes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculateurProjection.Confronter(
            new RequeteConfrontation(Base(100_000), centimes, new MoisCalendaire(2026, 2))));
    }

    [Theory]
    [InlineData(2025, 12)]   // avant l'horizon
    [InlineData(2026, 7)]    // après l'horizon (6 mois depuis janvier → juin)
    public void Un_mois_cible_hors_horizon_est_refuse(int annee, int mois)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculateurProjection.Confronter(
            new RequeteConfrontation(Base(100_000), 10_000, new MoisCalendaire(annee, mois))));
    }
}
