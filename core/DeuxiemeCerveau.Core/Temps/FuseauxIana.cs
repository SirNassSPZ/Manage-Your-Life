using System.Collections.Concurrent;

namespace DeuxiemeCerveau.Core.Temps;

/// <summary>
/// Résolution stricte des identifiants de fuseau IANA (règle 7, D-002).
/// Les identifiants Windows (« Romance Standard Time ») sont rejetés même quand la plateforme
/// saurait les résoudre : un identifiant non portable est une divergence en puissance.
/// « UTC » est accepté comme alias explicite.
/// </summary>
public static class FuseauxIana
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo?> Cache = new(StringComparer.Ordinal);

    public static bool EstValide(string? id) => id is not null && Resoudre(id) is not null;

    /// <summary>Renvoie le fuseau, ou null si l'identifiant n'est pas un identifiant IANA valide.</summary>
    public static TimeZoneInfo? Resoudre(string id)
        => Cache.GetOrAdd(id, static cle =>
        {
            if (string.IsNullOrWhiteSpace(cle))
                return null;
            if (cle == "UTC")
                return TimeZoneInfo.Utc;
            // Un identifiant Windows se convertit vers IANA ; un identifiant IANA, non.
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(cle, out _))
                return null;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(cle);
            }
            catch (TimeZoneNotFoundException)
            {
                return null;
            }
            catch (InvalidTimeZoneException)
            {
                return null;
            }
        });

    /// <summary>Résout ou lève — pour les chemins où la validation a déjà eu lieu.</summary>
    public static TimeZoneInfo Exiger(string id)
        => Resoudre(id) ?? throw new ArgumentException($"Fuseau IANA invalide : « {id} ».", nameof(id));
}
