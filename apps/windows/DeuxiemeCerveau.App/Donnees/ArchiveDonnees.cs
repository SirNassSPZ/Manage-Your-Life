using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeuxiemeCerveau.App.Donnees;

/// <summary>
/// Contenu de <c>donnees.json</c> dans l'archive d'export (§5.7). Format ouvert et lisible sans l'app :
/// chaque entité est stockée telle quelle (objet JSON canonique), corbeille comprise (les supprimés y
/// figurent, marqués). Version de format en tête pour la compatibilité future.
/// </summary>
public sealed record ArchiveDonnees(
    [property: JsonPropertyName("version_format")] int VersionFormat,
    [property: JsonPropertyName("exporte_le")] DateTimeOffset ExporteLe,
    IReadOnlyList<JsonElement> Elements,
    IReadOnlyList<JsonElement> Categories,
    IReadOnlyList<JsonElement> Projets,
    IReadOnlyList<JsonElement> Budgets,
    [property: JsonPropertyName("pieces_jointes")] IReadOnlyList<JsonElement> PiecesJointes,
    JsonElement? Reglage);
