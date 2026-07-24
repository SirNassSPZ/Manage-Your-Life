using DeuxiemeCerveau.Core.Migrations;
using Microsoft.Data.Sqlite;

namespace DeuxiemeCerveau.App.Local;

/// <summary>
/// Base locale de l'app (SQLite) — support du filet 1 (§6 : écriture locale d'abord). Ouvre la
/// connexion et applique les migrations au démarrage (§9, à l'identique du serveur). L'app utilise une
/// base par installation ; le chemin du fichier est fourni par la plateforme (coquille WinUI), jamais
/// codé en dur (règle 4). Les tests utilisent une base « :memory: » (connexion maintenue ouverte).
/// </summary>
public sealed class BaseLocale : IDisposable
{
    private readonly SqliteConnection _connexion;

    public BaseLocale(string chaineConnexion)
    {
        _connexion = new SqliteConnection(chaineConnexion);
        _connexion.Open();
        Migrations = new CibleMigrationLocale(_connexion).AppliquerAuDemarrage();
        Depot = new DepotLocal(_connexion);
    }

    /// <summary>Le dépôt local : entités, outbox, curseur de synchro, réglage.</summary>
    public DepotLocal Depot { get; }

    /// <summary>Migrations appliquées à l'ouverture (diagnostic).</summary>
    public IReadOnlyList<Migration> Migrations { get; }

    public void Dispose() => _connexion.Dispose();
}
