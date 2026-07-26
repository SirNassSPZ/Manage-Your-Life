namespace DeuxiemeCerveau.Core.Migrations;

/// <summary>
/// Migration 004 — colonnes <c>ordre</c> et <c>icone</c> sur <c>categories</c> (§3.3 et §9, v3.3,
/// décision D-030). Additive uniquement (règle 18) : deux colonnes acceptant NULL, aucune
/// suppression, aucun renommage. Les deux champs sont facultatifs — une catégorie sans
/// <c>ordre</c> se classe par nom, une catégorie sans <c>icone</c> s'affiche sans pictogramme —
/// donc les lignes déjà présentes restent valides telles quelles, sans reprise de données.
/// </summary>
public static class Migration004CategorieOrdreIcone
{
    public static readonly Migration Definition = new(
        Numero: 4,
        Nom: "categorie_ordre_icone",
        SqlAzure: SqlAzure,
        SqlLocal: SqlLocal);

    // Une colonne par instruction : SQLite n'accepte qu'un seul ADD COLUMN par ALTER TABLE, et
    // écrire les deux dialectes de la même façon garde la parité structurelle lisible (D-008).
    private const string SqlAzure = """
        ALTER TABLE categories ADD ordre INT NULL;
        ALTER TABLE categories ADD icone NVARCHAR(16) NULL;
        """;

    private const string SqlLocal = """
        ALTER TABLE categories ADD COLUMN ordre INTEGER NULL;
        ALTER TABLE categories ADD COLUMN icone TEXT NULL;
        """;
}
