using System.IO.Compression;
using DeuxiemeCerveau.App.Donnees;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Services;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.App.Tests;

/// <summary>
/// Export → import (§5.7, NON NÉGOCIABLE) — Étape 4e. Le scénario de parité solo « export réseau coupé
/// puis import sur installation vierge → contenu identique, corbeille comprise » (§12, critère « fini »).
/// Tout se fait en local, sans réseau.
/// </summary>
public sealed class ExportImportTests
{
    private static ServiceSaisie Saisie(BaseLocale b)
        => new(b.Depot, new IdentiteAppareil(b.Depot), new HorlogeFixe(FabriqueLocale.T0));

    [Fact]
    public void Export_puis_import_sur_installation_vierge_restitue_tout_corbeille_comprise()
    {
        using var a = FabriqueLocale.BaseMemoire();
        var saisie = Saisie(a);

        var vivant = FabriqueLocale.NouvelleFacture(titre: "loyer");
        saisie.Enregistrer(vivant, EntiteSynchro.Element);
        var aJeter = FabriqueLocale.NouvelleFacture(titre: "annulé");
        saisie.Enregistrer(aJeter, EntiteSynchro.Element);
        saisie.Supprimer(EntiteSynchro.Element, aJeter.Id); // à la corbeille
        saisie.Enregistrer(
            new Categorie { Nom = "Maison", Couleur = "#3366FF", Origine = OrigineCategorie.Transversale },
            EntiteSynchro.Categorie);
        saisie.Enregistrer(FabriqueLocale.Reglage(150000), EntiteSynchro.Reglage);

        // Export (sans réseau) vers un flux mémoire, puis import dans une installation VIERGE.
        using var flux = new MemoryStream();
        new ServiceExport(a.Depot, new HorlogeFixe(FabriqueLocale.T0)).Exporter(flux);
        flux.Position = 0;
        using var b = FabriqueLocale.BaseMemoire();
        new ServiceImport(b.Depot).Importer(flux);

        var elemsB = b.Depot.Enumerer(EntiteSynchro.Element);
        Assert.Equal(2, elemsB.Count);
        Assert.Contains(elemsB, e => e.Supprime);                                    // la corbeille est restituée
        Assert.Single(b.Depot.Enumerer(EntiteSynchro.Categorie));
        Assert.NotNull(b.Depot.Obtenir(EntiteSynchro.Reglage, ReglageSolde.IdSoldeReference));

        // Fidélité aller-retour : payloads identiques entité par entité.
        foreach (var ea in a.Depot.Enumerer(EntiteSynchro.Element))
        {
            var eb = b.Depot.Obtenir(EntiteSynchro.Element, ea.Id);
            Assert.NotNull(eb);
            Assert.Equal(ea.PayloadCanonique, eb.PayloadCanonique);
        }
    }

    [Fact]
    public void L_archive_contient_donnees_json_et_le_dossier_pieces_jointes()
    {
        using var a = FabriqueLocale.BaseMemoire();
        Saisie(a).Enregistrer(FabriqueLocale.NouvelleFacture(), EntiteSynchro.Element);

        using var flux = new MemoryStream();
        new ServiceExport(a.Depot, new HorlogeFixe(FabriqueLocale.T0)).Exporter(flux);
        flux.Position = 0;

        using var zip = new ZipArchive(flux, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("donnees.json"));
        Assert.NotNull(zip.GetEntry("pieces_jointes/"));
    }

    [Fact]
    public void Import_d_une_archive_sans_donnees_json_echoue_proprement()
    {
        using var flux = new MemoryStream();
        using (var zip = new ZipArchive(flux, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("autre.txt");
        flux.Position = 0;

        using var b = FabriqueLocale.BaseMemoire();
        Assert.Throws<ErreurImport>(() => new ServiceImport(b.Depot).Importer(flux));
    }
}
