using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Recalage du solde de référence (feuille de route Palier 1 rang 6 / reco #9, §3.4) — Étape 4f.
/// « L'unique geste de correction » : on vérifie l'aperçu de l'écart et le fait qu'appliquer remplace
/// bien le point de départ (dernier recalage gagne).
/// </summary>
public sealed class RecalageTests : IDisposable
{
    private readonly BaseLocale _base = FabriqueLocale.BaseMemoire();
    private readonly ServiceAujourdhui _accueil;
    private readonly ServiceRecalage _recalage;
    private readonly ServiceDemarrage _demarrage;

    public RecalageTests()
    {
        var id = new IdentiteAppareil(_base.Depot);
        var saisie = new ServiceSaisie(_base.Depot, id, new HorlogeFixe(FabriqueLocale.T0));
        _accueil = new ServiceAujourdhui(_base.Depot, new ServiceCalendrier(_base.Depot));
        _demarrage = new ServiceDemarrage(_base.Depot, saisie);
        _recalage = new ServiceRecalage(_accueil, _demarrage);
    }

    public void Dispose() => _base.Dispose();

    [Fact]
    public void Au_premier_reglage_il_n_y_a_pas_d_ecart_a_montrer()
    {
        var etat = _recalage.Preparer(248360, new DateOnly(2026, 7, 24));

        Assert.Null(etat.AncienCentimes);
        Assert.Null(etat.EcartCentimes); // rien à corriger : c'est le point de départ
        Assert.Equal(248360, etat.NouveauCentimes);
    }

    [Fact]
    public void L_ecart_est_montre_tel_quel_dans_les_deux_sens()
    {
        _demarrage.DefinirSoldeReference(250000, new DateOnly(2026, 7, 1));

        var enMoins = _recalage.Preparer(248360, new DateOnly(2026, 7, 24));
        Assert.Equal(250000, enMoins.AncienCentimes);
        Assert.Equal(new DateOnly(2026, 7, 1), enMoins.AncienneDate);
        Assert.Equal(-1640, enMoins.EcartCentimes); // il manque 16,40 € — constaté, pas jugé

        var enPlus = _recalage.Preparer(251200, new DateOnly(2026, 7, 24));
        Assert.Equal(1200, enPlus.EcartCentimes);
    }

    [Fact]
    public void Preparer_n_ecrit_rien_seul_appliquer_recale()
    {
        _demarrage.DefinirSoldeReference(250000, new DateOnly(2026, 7, 1));

        _recalage.Preparer(199999, new DateOnly(2026, 7, 24));
        Assert.Equal(250000, _accueil.SoldeDeReference()!.Centimes); // l'aperçu n'a rien changé

        Assert.True(_recalage.Appliquer(199999, new DateOnly(2026, 7, 24)).Reussi);
        var apres = _accueil.SoldeDeReference()!;
        Assert.Equal(199999, apres.Centimes);                        // dernier recalage gagne (§3.4)
        Assert.Equal(new DateOnly(2026, 7, 24), apres.Date);
    }
}
