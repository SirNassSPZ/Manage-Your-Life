namespace DeuxiemeCerveau.Core.Migrations;

/// <summary>
/// Migration 001 — schéma initial V1, transcription du DDL de référence (§9) avec les affinements
/// documentés en D-008 (sémantique inchangée) : colonne « entite » sur change_log, champs
/// d'audit/synchro explicités sur categories/projets/budgets/attachments, settings complétée,
/// colonne JSON « rappels » sur elements, index de pull sur chaque table synchronisée.
/// Les bases locales reprennent la même structure, plus « outbox » et « sync_etat » (§9).
/// </summary>
public static class Migration001SchemaInitial
{
    public static readonly Migration Definition = new(
        Numero: 1,
        Nom: "schema_initial",
        SqlAzure: SqlAzure,
        SqlLocal: SqlLocal);

    private const string SqlAzure = """
        CREATE TABLE elements (
          id                 UNIQUEIDENTIFIER PRIMARY KEY,
          type               NVARCHAR(20)  NOT NULL,
          titre              NVARCHAR(300) NOT NULL,
          description        NVARCHAR(MAX) NULL,
          date_debut         DATETIME2     NULL,          -- UTC
          date_fin           DATETIME2     NULL,          -- UTC
          fuseau             NVARCHAR(64)  NULL,          -- IANA
          journee_entiere    BIT NOT NULL DEFAULT 0,
          date_approximative BIT NOT NULL DEFAULT 0,
          recurrence         NVARCHAR(500) NULL,          -- RRULE
          montant_centimes   BIGINT        NULL,
          devise             CHAR(3)       NULL,
          sens               NVARCHAR(10)  NULL,          -- entree | sortie
          projet_id          UNIQUEIDENTIFIER NULL,
          budget_id          UNIQUEIDENTIFIER NULL,
          est_obligatoire    BIT NOT NULL DEFAULT 0,
          score_points       INT           NULL,
          priorite           NVARCHAR(10)  NULL,
          ordre_manuel       INT           NULL,
          statut             NVARCHAR(20)  NOT NULL,
          rappels            NVARCHAR(MAX) NULL,          -- JSON (D-008)
          date_creation      DATETIME2     NOT NULL,
          date_modification  DATETIME2     NOT NULL,
          appareil_source    UNIQUEIDENTIFIER NOT NULL,
          version            INT           NOT NULL,
          server_seq         BIGINT        NOT NULL,      -- index pour le pull
          supprime           BIT NOT NULL DEFAULT 0,
          date_suppression   DATETIME2     NULL
        );
        CREATE INDEX ix_elements_seq ON elements(server_seq);

        CREATE TABLE categories (
          id                UNIQUEIDENTIFIER PRIMARY KEY,
          nom               NVARCHAR(100) NOT NULL,
          couleur           CHAR(7)       NOT NULL,
          origine           NVARCHAR(15)  NOT NULL,       -- transversale | projet
          date_creation     DATETIME2     NOT NULL,
          date_modification DATETIME2     NOT NULL,
          appareil_source   UNIQUEIDENTIFIER NOT NULL,
          version           INT           NOT NULL,
          server_seq        BIGINT        NOT NULL,
          supprime          BIT NOT NULL DEFAULT 0,
          date_suppression  DATETIME2     NULL
        );
        CREATE INDEX ix_categories_seq ON categories(server_seq);

        CREATE TABLE element_categories (
          element_id   UNIQUEIDENTIFIER NOT NULL,
          categorie_id UNIQUEIDENTIFIER NOT NULL,
          PRIMARY KEY (element_id, categorie_id)
        );

        CREATE TABLE projets (
          id                UNIQUEIDENTIFIER PRIMARY KEY,
          nom               NVARCHAR(150) NOT NULL,
          couleur           CHAR(7)       NOT NULL,
          categorie_id      UNIQUEIDENTIFIER NULL,
          statut            NVARCHAR(15)  NOT NULL,       -- actif | en_pause | termine
          date_creation     DATETIME2     NOT NULL,
          date_modification DATETIME2     NOT NULL,
          appareil_source   UNIQUEIDENTIFIER NOT NULL,
          version           INT           NOT NULL,
          server_seq        BIGINT        NOT NULL,
          supprime          BIT NOT NULL DEFAULT 0,
          date_suppression  DATETIME2     NULL
        );
        CREATE INDEX ix_projets_seq ON projets(server_seq);

        CREATE TABLE budgets (
          id                       UNIQUEIDENTIFIER PRIMARY KEY,
          nom                      NVARCHAR(100) NOT NULL,
          couleur                  CHAR(7)       NOT NULL,
          montant_periode_centimes BIGINT        NOT NULL,
          periode                  NVARCHAR(15)  NOT NULL, -- mensuel
          statut                   NVARCHAR(15)  NOT NULL, -- actif | archive
          date_creation            DATETIME2     NOT NULL,
          date_modification        DATETIME2     NOT NULL,
          appareil_source          UNIQUEIDENTIFIER NOT NULL,
          version                  INT           NOT NULL,
          server_seq               BIGINT        NOT NULL,
          supprime                 BIT NOT NULL DEFAULT 0,
          date_suppression         DATETIME2     NULL
        );
        CREATE INDEX ix_budgets_seq ON budgets(server_seq);

        CREATE TABLE attachments (
          id                UNIQUEIDENTIFIER PRIMARY KEY,
          element_id        UNIQUEIDENTIFIER NOT NULL,
          nom_fichier       NVARCHAR(255) NOT NULL,
          taille_octets     BIGINT        NOT NULL,
          blob_path         NVARCHAR(400) NOT NULL,
          confirme          BIT NOT NULL DEFAULT 0,
          date_creation     DATETIME2     NOT NULL,
          date_modification DATETIME2     NOT NULL,
          appareil_source   UNIQUEIDENTIFIER NOT NULL,
          version           INT           NOT NULL,
          server_seq        BIGINT        NOT NULL,
          supprime          BIT NOT NULL DEFAULT 0,
          date_suppression  DATETIME2     NULL
        );
        CREATE INDEX ix_attachments_seq ON attachments(server_seq);

        CREATE TABLE devices (
          id                  UNIQUEIDENTIFIER PRIMARY KEY,
          nom                 NVARCHAR(100) NOT NULL,
          plateforme          NVARCHAR(20)  NOT NULL,
          date_enregistrement DATETIME2     NOT NULL
        );

        CREATE TABLE change_log (
          server_seq  BIGINT IDENTITY PRIMARY KEY,
          change_id   UNIQUEIDENTIFIER NOT NULL UNIQUE,   -- idempotence
          entite      NVARCHAR(20) NOT NULL DEFAULT 'element', -- D-006
          element_id  UNIQUEIDENTIFIER NOT NULL,          -- identifiant d'entité (généralisé, D-006)
          payload     NVARCHAR(MAX) NOT NULL,             -- version complète (JSON)
          appareil_id UNIQUEIDENTIFIER NOT NULL,
          resultat    NVARCHAR(20) NOT NULL,              -- applique | perdant_archive
          recu_le     DATETIME2 NOT NULL
        );

        CREATE TABLE settings (
          cle               NVARCHAR(50) PRIMARY KEY,     -- ex. solde_reference
          valeur            NVARCHAR(MAX) NOT NULL,
          date_modification DATETIME2 NOT NULL,
          date_creation     DATETIME2 NULL,
          appareil_source   UNIQUEIDENTIFIER NULL,
          version           INT NOT NULL DEFAULT 1,
          server_seq        BIGINT NOT NULL DEFAULT 0,
          supprime          BIT NOT NULL DEFAULT 0,
          date_suppression  DATETIME2 NULL
        );
        CREATE INDEX ix_settings_seq ON settings(server_seq);
        """;

    private const string SqlLocal = """
        CREATE TABLE elements (
          id                 TEXT PRIMARY KEY,
          type               TEXT    NOT NULL,
          titre              TEXT    NOT NULL,
          description        TEXT    NULL,
          date_debut         TEXT    NULL,                -- UTC
          date_fin           TEXT    NULL,                -- UTC
          fuseau             TEXT    NULL,                -- IANA
          journee_entiere    INTEGER NOT NULL DEFAULT 0,
          date_approximative INTEGER NOT NULL DEFAULT 0,
          recurrence         TEXT    NULL,                -- RRULE
          montant_centimes   INTEGER NULL,
          devise             TEXT    NULL,
          sens               TEXT    NULL,                -- entree | sortie
          projet_id          TEXT    NULL,
          budget_id          TEXT    NULL,
          est_obligatoire    INTEGER NOT NULL DEFAULT 0,
          score_points       INTEGER NULL,
          priorite           TEXT    NULL,
          ordre_manuel       INTEGER NULL,
          statut             TEXT    NOT NULL,
          rappels            TEXT    NULL,                -- JSON (D-008)
          date_creation      TEXT    NOT NULL,
          date_modification  TEXT    NOT NULL,
          appareil_source    TEXT    NOT NULL,
          version            INTEGER NOT NULL,
          server_seq         INTEGER NOT NULL,            -- index pour le pull
          supprime           INTEGER NOT NULL DEFAULT 0,
          date_suppression   TEXT    NULL
        );
        CREATE INDEX ix_elements_seq ON elements(server_seq);

        CREATE TABLE categories (
          id                TEXT PRIMARY KEY,
          nom               TEXT    NOT NULL,
          couleur           TEXT    NOT NULL,
          origine           TEXT    NOT NULL,             -- transversale | projet
          date_creation     TEXT    NOT NULL,
          date_modification TEXT    NOT NULL,
          appareil_source   TEXT    NOT NULL,
          version           INTEGER NOT NULL,
          server_seq        INTEGER NOT NULL,
          supprime          INTEGER NOT NULL DEFAULT 0,
          date_suppression  TEXT    NULL
        );
        CREATE INDEX ix_categories_seq ON categories(server_seq);

        CREATE TABLE element_categories (
          element_id   TEXT NOT NULL,
          categorie_id TEXT NOT NULL,
          PRIMARY KEY (element_id, categorie_id)
        );

        CREATE TABLE projets (
          id                TEXT PRIMARY KEY,
          nom               TEXT    NOT NULL,
          couleur           TEXT    NOT NULL,
          categorie_id      TEXT    NULL,
          statut            TEXT    NOT NULL,             -- actif | en_pause | termine
          date_creation     TEXT    NOT NULL,
          date_modification TEXT    NOT NULL,
          appareil_source   TEXT    NOT NULL,
          version           INTEGER NOT NULL,
          server_seq        INTEGER NOT NULL,
          supprime          INTEGER NOT NULL DEFAULT 0,
          date_suppression  TEXT    NULL
        );
        CREATE INDEX ix_projets_seq ON projets(server_seq);

        CREATE TABLE budgets (
          id                       TEXT PRIMARY KEY,
          nom                      TEXT    NOT NULL,
          couleur                  TEXT    NOT NULL,
          montant_periode_centimes INTEGER NOT NULL,
          periode                  TEXT    NOT NULL,      -- mensuel
          statut                   TEXT    NOT NULL,      -- actif | archive
          date_creation            TEXT    NOT NULL,
          date_modification        TEXT    NOT NULL,
          appareil_source          TEXT    NOT NULL,
          version                  INTEGER NOT NULL,
          server_seq               INTEGER NOT NULL,
          supprime                 INTEGER NOT NULL DEFAULT 0,
          date_suppression         TEXT    NULL
        );
        CREATE INDEX ix_budgets_seq ON budgets(server_seq);

        CREATE TABLE attachments (
          id                TEXT PRIMARY KEY,
          element_id        TEXT    NOT NULL,
          nom_fichier       TEXT    NOT NULL,
          taille_octets     INTEGER NOT NULL,
          blob_path         TEXT    NOT NULL,
          confirme          INTEGER NOT NULL DEFAULT 0,
          date_creation     TEXT    NOT NULL,
          date_modification TEXT    NOT NULL,
          appareil_source   TEXT    NOT NULL,
          version           INTEGER NOT NULL,
          server_seq        INTEGER NOT NULL,
          supprime          INTEGER NOT NULL DEFAULT 0,
          date_suppression  TEXT    NULL
        );
        CREATE INDEX ix_attachments_seq ON attachments(server_seq);

        CREATE TABLE devices (
          id                  TEXT PRIMARY KEY,
          nom                 TEXT NOT NULL,
          plateforme          TEXT NOT NULL,
          date_enregistrement TEXT NOT NULL
        );

        CREATE TABLE change_log (
          server_seq  INTEGER PRIMARY KEY AUTOINCREMENT,
          change_id   TEXT NOT NULL UNIQUE,               -- idempotence
          entite      TEXT NOT NULL DEFAULT 'element',    -- D-006
          element_id  TEXT NOT NULL,                      -- identifiant d'entité (généralisé, D-006)
          payload     TEXT NOT NULL,                      -- version complète (JSON)
          appareil_id TEXT NOT NULL,
          resultat    TEXT NOT NULL,                      -- applique | perdant_archive
          recu_le     TEXT NOT NULL
        );

        CREATE TABLE settings (
          cle               TEXT PRIMARY KEY,             -- ex. solde_reference
          valeur            TEXT NOT NULL,
          date_modification TEXT NOT NULL,
          date_creation     TEXT NULL,
          appareil_source   TEXT NULL,
          version           INTEGER NOT NULL DEFAULT 1,
          server_seq        INTEGER NOT NULL DEFAULT 0,
          supprime          INTEGER NOT NULL DEFAULT 0,
          date_suppression  TEXT NULL
        );
        CREATE INDEX ix_settings_seq ON settings(server_seq);

        -- Tables locales uniquement (§9 : « les bases locales reprennent la même structure,
        -- plus la table outbox » ; sync_etat porte le curseur de pull et l'appareil_id, D-008).
        CREATE TABLE outbox (
          change_id         TEXT PRIMARY KEY,
          entite            TEXT    NOT NULL DEFAULT 'element',
          element_id        TEXT    NOT NULL,
          version           INTEGER NOT NULL,
          payload           TEXT    NOT NULL,
          date_modification TEXT    NOT NULL,
          appareil_id       TEXT    NOT NULL,
          cree_le           TEXT    NOT NULL
        );

        CREATE TABLE sync_etat (
          cle    TEXT PRIMARY KEY,                        -- ex. curseur_pull, appareil_id
          valeur TEXT NOT NULL
        );
        """;
}
