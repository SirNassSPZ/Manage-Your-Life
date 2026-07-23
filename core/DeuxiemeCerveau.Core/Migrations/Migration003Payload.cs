namespace DeuxiemeCerveau.Core.Migrations;

/// <summary>
/// Migration 003 — colonne <c>payload</c> (JSON canonique) sur chaque table d'entité synchronisée.
/// Additive uniquement (règle 18). Le serveur y stocke la version canonique de l'entité : c'est la
/// source de lecture (fidélité aller-retour garantie), les colonnes typées §9 restant peuplées pour
/// la conformité au schéma et l'indexation (D-012). Le réglage utilise déjà <c>settings.valeur</c>.
/// </summary>
public static class Migration003Payload
{
    public static readonly Migration Definition = new(
        Numero: 3,
        Nom: "payload_canonique",
        SqlAzure: SqlAzure,
        SqlLocal: SqlLocal);

    private const string SqlAzure = """
        ALTER TABLE elements    ADD payload NVARCHAR(MAX) NULL;
        ALTER TABLE categories  ADD payload NVARCHAR(MAX) NULL;
        ALTER TABLE projets     ADD payload NVARCHAR(MAX) NULL;
        ALTER TABLE budgets     ADD payload NVARCHAR(MAX) NULL;
        ALTER TABLE attachments ADD payload NVARCHAR(MAX) NULL;
        """;

    private const string SqlLocal = """
        ALTER TABLE elements    ADD COLUMN payload TEXT NULL;
        ALTER TABLE categories  ADD COLUMN payload TEXT NULL;
        ALTER TABLE projets     ADD COLUMN payload TEXT NULL;
        ALTER TABLE budgets     ADD COLUMN payload TEXT NULL;
        ALTER TABLE attachments ADD COLUMN payload TEXT NULL;
        """;
}
