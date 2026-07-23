using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeuxiemeCerveau.Core.Json;

/// <summary>
/// Horodatages UTC ISO 8601 avec suffixe « Z » (règle 7, D-007) :
/// « 2026-07-23T10:00:00Z », fractions de seconde omises si nulles.
/// Toute valeur lue est normalisée en UTC ; une chaîne sans indication de fuseau est rejetée.
/// </summary>
public sealed class ConvertisseurDateUtc : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var texte = reader.GetString()
            ?? throw new JsonException("Horodatage attendu (chaîne ISO 8601 UTC).");
        if (!DateTimeOffset.TryParse(texte, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var valeur))
            throw new JsonException($"Horodatage invalide : « {texte} ».");
        // RoundtripKind laisse passer les chaînes sans fuseau (Kind=Unspecified → offset local supposé) :
        // on exige une indication explicite (Z ou offset).
        if (!texte.EndsWith('Z') && !texte.Contains('+') && texte.LastIndexOf('-') <= 9)
            throw new JsonException($"Horodatage sans fuseau explicite : « {texte} » (Z ou offset requis).");
        return valeur.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(Formater(value));

    public static string Formater(DateTimeOffset valeur)
    {
        var utc = valeur.UtcDateTime;
        return utc.Ticks % TimeSpan.TicksPerSecond == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }
}
