using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// <see cref="Format"/> est la SEULE porte entre le texte et les centimes entiers. La règle 5
/// (« argent en centimes entiers, jamais de flottant ») ne tient que si cette porte tient.
/// </summary>
public class FormatTests
{
    [Theory]
    [InlineData("800", 80_000)]
    [InlineData("800,50", 80_050)]
    [InlineData("1 200,50", 120_050)] // espace fine insécable, tel que l'app l'affiche
    [InlineData("1 200,50", 120_050)] // espace insécable, tel qu'un copier-coller le donne
    [InlineData("1 200,50", 120_050)]      // espace ordinaire, tel qu'on le tape
    [InlineData("800.50", 80_050)]         // point décimal : clavier numérique
    [InlineData("29,99 €", 2_999)]         // symbole recollé depuis un affichage
    [InlineData("  42  ", 4_200)]
    [InlineData("0", 0)]
    [InlineData("-15,20", -1_520)]         // découvert réel, autorisé (D-004)
    public void Lit_les_formes_courantes_en_centimes(string saisie, long attendu)
    {
        Assert.True(Format.TryCentimes(saisie, out var centimes));
        Assert.Equal(attendu, centimes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("12,34,56")]
    public void Refuse_ce_qui_n_est_pas_un_montant(string? saisie)
    {
        Assert.False(Format.TryCentimes(saisie, out var centimes));
        Assert.Equal(0, centimes);
    }

    [Theory]
    [InlineData("0,005", 1)]      // arrondi au centime supérieur, jamais tronqué
    [InlineData("0,004", 0)]
    [InlineData("10,999", 1_100)]
    public void Arrondit_au_centime_sans_jamais_garder_de_flottant(string saisie, long attendu)
    {
        Assert.True(Format.TryCentimes(saisie, out var centimes));
        Assert.Equal(attendu, centimes);
    }

    [Fact]
    public void Le_signe_suit_le_sens_et_utilise_le_moins_typographique()
    {
        Assert.StartsWith("+", Format.EurosSigne(2_380_00, Sens.Entree));
        Assert.StartsWith("−", Format.EurosSigne(840_00, Sens.Sortie));   // U+2212, pas un trait d'union
        Assert.DoesNotContain("-", Format.EurosSigne(840_00, Sens.Sortie));
    }

    [Fact]
    public void Une_cloture_negative_ressort_signee()
    {
        Assert.StartsWith("−", Format.EurosRelatif(-18_000));
        Assert.DoesNotContain("−", Format.EurosRelatif(18_000));
    }

    [Theory]
    [InlineData(240_000, Sens.Entree, "+2 400")]
    [InlineData(84_000, Sens.Sortie, "−840")]
    [InlineData(3_250, Sens.Sortie, "−33")]     // arrondi à l'euro : la case est étroite
    public void La_forme_compacte_du_calendrier_arrondit_a_l_euro(long centimes, Sens sens, string attendu)
    {
        // Espaces insécables normalisés : c'est le groupement qui est testé, pas le codet.
        Assert.Equal(attendu, Format.EurosCompact(centimes, sens).Replace(' ', ' ').Replace(' ', ' '));
    }

    [Fact]
    public void Les_capitales_gardent_les_accents()
    {
        // « AOUT » au lieu de « AOÛT » est la faute classique d'un ToUpper invariant.
        Assert.Equal("AOÛT", Format.Capitales("août"));
        Assert.Equal("AUJOURD'HUI · JEU. 24", Format.Capitales("aujourd'hui · jeu. 24"));
    }

    [Theory]
    [InlineData("2026-08", "août 26")]
    [InlineData("2026-01", "janv. 26")]
    public void Abrege_une_cle_de_mois_du_contrat_API(string cle, string attendu)
    {
        Assert.Equal(attendu, Format.MoisAbrege(cle));
    }

    [Fact]
    public void Une_cle_de_mois_illisible_est_rendue_telle_quelle_plutot_que_de_planter()
    {
        Assert.Equal("pas-un-mois", Format.MoisAbrege("pas-un-mois"));
    }

    [Fact]
    public void La_forme_titree_capitalise_l_initiale()
    {
        Assert.Equal("Août 26", Format.MoisAbregeTitre("2026-08"));
    }
}
