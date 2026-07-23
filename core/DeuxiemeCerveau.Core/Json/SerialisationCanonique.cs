using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeuxiemeCerveau.Core.Json;

/// <summary>
/// Le contrat JSON canonique du projet (D-007) : noms français en snake_case exactement conformes à la spec,
/// dates UTC « Z », montants entiers, null omis, champs inconnus rejetés (échec bruyant plutôt
/// qu'écrasement silencieux — voir D-007).
/// </summary>
public static class SerialisationCanonique
{
    public static readonly JsonSerializerOptions Options = Creer();

    private static JsonSerializerOptions Creer()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.Converters.Add(new ConvertisseurDateUtc());
        return options;
    }

    public static string Serialiser<T>(T valeur) => JsonSerializer.Serialize(valeur, Options);

    /// <summary>Désérialise en rejetant les champs inconnus et les valeurs hors contrat (JsonException).</summary>
    public static T Deserialiser<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException("Payload JSON nul.");

    public static T Deserialiser<T>(JsonElement element) => element.Deserialize<T>(Options)
        ?? throw new JsonException("Payload JSON nul.");
}
