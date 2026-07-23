namespace DeuxiemeCerveau.Core.Temps;

/// <summary>
/// Conversions heure locale ↔ UTC avec conventions DST fixées (D-002, identiques dans les deux apps) :
/// heure locale inexistante → décalée de la durée du saut ; heure ambiguë → première occurrence
/// (instant UTC le plus tôt).
/// </summary>
public static class ConvertisseurFuseau
{
    /// <summary>Convertit une heure murale locale (Kind non spécifié) en instant UTC.</summary>
    public static DateTimeOffset VersUtc(DateTime locale, TimeZoneInfo fuseau)
    {
        if (locale.Kind != DateTimeKind.Unspecified)
            locale = DateTime.SpecifyKind(locale, DateTimeKind.Unspecified);

        if (fuseau.IsInvalidTime(locale))
        {
            // Passage à l'heure d'été : l'heure n'existe pas — on la décale de la durée du saut.
            // (± 3 h encadre toute transition réelle ; aucun fuseau n'a deux transitions si proches.)
            var offsetApres = fuseau.GetUtcOffset(locale.AddHours(3));
            var offsetAvant = fuseau.GetUtcOffset(locale.AddHours(-3));
            var saut = offsetApres - offsetAvant;
            var decalee = locale + saut;
            return new DateTimeOffset(decalee.Ticks - offsetApres.Ticks, TimeSpan.Zero);
        }

        if (fuseau.IsAmbiguousTime(locale))
        {
            // Retour à l'heure d'hiver : première occurrence = offset le plus grand → instant le plus tôt.
            var premierOffset = fuseau.GetAmbiguousTimeOffsets(locale).Max();
            return new DateTimeOffset(locale.Ticks - premierOffset.Ticks, TimeSpan.Zero);
        }

        var offset = fuseau.GetUtcOffset(locale);
        return new DateTimeOffset(locale.Ticks - offset.Ticks, TimeSpan.Zero);
    }

    /// <summary>Convertit un instant UTC en heure murale locale du fuseau.</summary>
    public static DateTime VersLocale(DateTimeOffset utc, TimeZoneInfo fuseau)
        => TimeZoneInfo.ConvertTime(utc, fuseau).DateTime;
}
