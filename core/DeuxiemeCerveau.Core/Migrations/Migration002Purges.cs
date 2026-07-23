namespace DeuxiemeCerveau.Core.Migrations;

/// <summary>
/// Migration 002 — pierres tombales de purge (§5.6, §9, décision D-010, spec v3.2).
/// La purge est arbitrée par le serveur : la tombale empêche la résurrection d'une entité purgée
/// par un appareil retardataire, et transporte la purge vers les autres appareils via le pull.
/// </summary>
public static class Migration002Purges
{
    public static readonly Migration Definition = new(
        Numero: 2,
        Nom: "purges",
        SqlAzure: SqlAzure,
        SqlLocal: SqlLocal);

    private const string SqlAzure = """
        CREATE TABLE purges (
          entite      NVARCHAR(20) NOT NULL,
          entite_id   UNIQUEIDENTIFIER NOT NULL,
          server_seq  BIGINT NOT NULL,
          change_id   UNIQUEIDENTIFIER NOT NULL UNIQUE,
          appareil_id UNIQUEIDENTIFIER NOT NULL,
          purge_le    DATETIME2 NOT NULL,
          PRIMARY KEY (entite, entite_id)
        );
        CREATE INDEX ix_purges_seq ON purges(server_seq);
        """;

    private const string SqlLocal = """
        CREATE TABLE purges (
          entite      TEXT    NOT NULL,
          entite_id   TEXT    NOT NULL,
          server_seq  INTEGER NOT NULL,
          change_id   TEXT    NOT NULL UNIQUE,
          appareil_id TEXT    NOT NULL,
          purge_le    TEXT    NOT NULL,
          PRIMARY KEY (entite, entite_id)
        );
        CREATE INDEX ix_purges_seq ON purges(server_seq);
        """;
}
