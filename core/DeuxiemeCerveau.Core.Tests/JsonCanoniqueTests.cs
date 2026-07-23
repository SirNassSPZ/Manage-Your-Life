using System.Text.Json;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using Xunit;

namespace DeuxiemeCerveau.Core.Tests;

/// <summary>
/// Le contrat JSON canonique (D-007) est verrouillé par ces tests : noms de champs français
/// exactement conformes à la spec, valeurs d'énumérations, format des dates. Toute divergence
/// ici est une divergence de contrat entre les deux apps.
/// </summary>
public class JsonCanoniqueTests
{
    [Fact]
    public void Noms_des_champs_de_l_element_exactement_conformes_au_3_1()
    {
        var element = Fabrique.Facture();
        element.Description = "desc";
        element.DateFin = element.DateDebut!.Value.AddHours(1);
        element.JourneeEntiere = false;
        element.Recurrence = "FREQ=MONTHLY";
        element.Categories.Add(Guid.NewGuid());
        element.ProjetId = Guid.NewGuid();
        element.BudgetId = Guid.NewGuid();
        element.PiecesJointes.Add(Guid.NewGuid());
        element.Rappels.Add(new Rappel { Type = TypeRappel.Relatif, MinutesAvant = 30 });
        element.ServerSeq = 42;

        var document = JsonDocument.Parse(SerialisationCanonique.Serialiser(element));
        var noms = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        HashSet<string> attendus =
        [
            "id", "type", "titre", "description",
            "date_debut", "date_fin", "fuseau", "journee_entiere", "date_approximative", "recurrence",
            "montant_centimes", "devise", "sens",
            "categories", "projet_id", "budget_id",
            "est_obligatoire", "pieces_jointes", "rappels", "statut",
            "date_creation", "date_modification", "appareil_source", "version", "server_seq", "supprime",
        ];
        // score_points, priorite, ordre_manuel, date_suppression : null → omis (D-007).
        Assert.Equal(attendus.Order(), noms.Order());
    }

    [Fact]
    public void Les_nuls_sont_omis_les_listes_vides_presentes()
    {
        var json = SerialisationCanonique.Serialiser(Fabrique.Note());
        var racine = JsonDocument.Parse(json).RootElement;

        Assert.False(racine.TryGetProperty("description", out _) && racine.GetProperty("description").ValueKind == JsonValueKind.Null);
        Assert.False(racine.TryGetProperty("montant_centimes", out _));
        Assert.False(racine.TryGetProperty("date_debut", out _));
        Assert.Equal(JsonValueKind.Array, racine.GetProperty("categories").ValueKind);
        Assert.Equal(JsonValueKind.Array, racine.GetProperty("rappels").ValueKind);
    }

    [Theory]
    [InlineData(TypeElement.Facture, "facture")]
    [InlineData(TypeElement.Rendezvous, "rendezvous")]
    [InlineData(TypeElement.Note, "note")]
    public void Types_en_snake_case(TypeElement valeur, string attendu)
        => Assert.Equal($"\"{attendu}\"", SerialisationCanonique.Serialiser(valeur));

    [Theory]
    [InlineData(StatutElement.AVenir, "a_venir")]
    [InlineData(StatutElement.Paye, "paye")]
    [InlineData(StatutElement.AFaire, "a_faire")]
    [InlineData(StatutElement.Reporte, "reporte")]
    [InlineData(StatutElement.Planifie, "planifie")]
    [InlineData(StatutElement.Planifiee, "planifiee")]
    [InlineData(StatutElement.Abandonnee, "abandonnee")]
    [InlineData(StatutElement.Archivee, "archivee")]
    public void Statuts_en_snake_case(StatutElement valeur, string attendu)
        => Assert.Equal($"\"{attendu}\"", SerialisationCanonique.Serialiser(valeur));

    [Theory]
    [InlineData(StatutProjet.EnPause, "en_pause")]
    [InlineData(OrigineCategorie.Transversale, "transversale")]
    [InlineData(PeriodeBudget.Mensuel, "mensuel")]
    public void Autres_enums_en_snake_case(object valeur, string attendu)
        => Assert.Equal($"\"{attendu}\"", JsonSerializer.Serialize(valeur, valeur.GetType(), SerialisationCanonique.Options));

    [Theory]
    [InlineData(EntiteSynchro.Element, "element")]
    [InlineData(EntiteSynchro.PieceJointe, "piece_jointe")]
    [InlineData(EntiteSynchro.Reglage, "reglage")]
    [InlineData(ResultatChangement.Applique, "applique")]
    [InlineData(ResultatChangement.PerdantArchive, "perdant_archive")]
    [InlineData(ResultatChangement.Purge, "purge")]
    [InlineData(ResultatChangement.RefusePurge, "refuse_purge")]
    [InlineData(StatutPurge.Purgee, "purgee")]
    [InlineData(StatutPurge.Refusee, "refusee")]
    public void Enums_de_synchro_en_snake_case(object valeur, string attendu)
        => Assert.Equal($"\"{attendu}\"", JsonSerializer.Serialize(valeur, valeur.GetType(), SerialisationCanonique.Options));

    // ----- Dates (règle 7, D-007) -----

    [Fact]
    public void Dates_utc_iso_8601_avec_z()
    {
        Assert.Equal("\"2026-07-23T10:00:00Z\"",
            SerialisationCanonique.Serialiser(new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero)));
        Assert.Equal("\"2026-07-23T10:00:00.5Z\"",
            SerialisationCanonique.Serialiser(new DateTimeOffset(2026, 7, 23, 10, 0, 0, 500, TimeSpan.Zero)));
    }

    [Fact]
    public void Date_avec_offset_normalisee_en_utc()
    {
        var lue = SerialisationCanonique.Deserialiser<DateTimeOffset>("\"2026-07-23T12:00:00+02:00\"");
        Assert.Equal(TimeSpan.Zero, lue.Offset);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero), lue);
    }

    [Fact]
    public void Date_sans_fuseau_explicite_rejetee()
        => Assert.Throws<JsonException>(
            () => SerialisationCanonique.Deserialiser<DateTimeOffset>("\"2026-07-23T10:00:00\""));

    [Fact]
    public void Date_calendaire_en_iso()
    {
        Assert.Equal("\"2026-07-01\"", SerialisationCanonique.Serialiser(new DateOnly(2026, 7, 1)));
        Assert.Equal(new DateOnly(2026, 7, 1), SerialisationCanonique.Deserialiser<DateOnly>("\"2026-07-01\""));
    }

    // ----- Robustesse -----

    [Fact]
    public void Aller_retour_stable()
    {
        var element = Fabrique.Facture(recurrence: "FREQ=MONTHLY;BYMONTHDAY=-1");
        element.Rappels.Add(new Rappel { Type = TypeRappel.Absolu, Date = Fabrique.T0 });
        var json1 = SerialisationCanonique.Serialiser(element);
        var json2 = SerialisationCanonique.Serialiser(SerialisationCanonique.Deserialiser<Element>(json1));
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Champ_inconnu_rejete()
        => Assert.Throws<JsonException>(() => SerialisationCanonique.Deserialiser<Rappel>(
            "{\"type\":\"relatif\",\"minutes_avant\":10,\"surprise\":1}"));

    [Fact]
    public void Enum_inconnue_rejetee()
        => Assert.Throws<JsonException>(() => SerialisationCanonique.Deserialiser<TypeElement>("\"cheque\""));

    [Fact]
    public void Montant_en_chaine_rejete()
        => Assert.Throws<JsonException>(() => SerialisationCanonique.Deserialiser<long>("\"80000\""));
}

public class Uuid5Tests
{
    [Fact]
    public void Deterministe_et_stable()
    {
        var a = Uuid5.Derive("cascade:x:y");
        var b = Uuid5.Derive("cascade:x:y");
        var c = Uuid5.Derive("cascade:x:z");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Version_5_et_variante_rfc()
    {
        var texte = Uuid5.Derive("test").ToString("D");
        Assert.Equal('5', texte[14]); // version
        Assert.Contains(texte[19], "89ab"); // variante RFC 4122
    }

    [Fact]
    public void Valeur_de_reference_inter_plateformes()
    {
        // UUIDv5 dans l'espace DNS RFC 4122 pour « www.example.org » — valeur publique connue.
        // L'implémentation Swift devra produire exactement la même (D-006).
        Assert.Equal(new Guid("74738ff5-5367-5958-9aee-98fffdcd1876"),
            Uuid5.Derive(new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8"), "www.example.org"));
    }
}
