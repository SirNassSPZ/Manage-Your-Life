using System.Globalization;

namespace DeuxiemeCerveau.Core.Recurrence;

/// <summary>Erreur d'analyse ou d'usage d'une RRULE — toujours bruyante (D-003).</summary>
public sealed class ErreurRecurrence(string message) : Exception(message);

public enum FrequenceRecurrence
{
    Quotidienne,  // DAILY
    Hebdomadaire, // WEEKLY
    Mensuelle,    // MONTHLY
    Annuelle,     // YEARLY
}

/// <summary>Jour de semaine avec ordinal optionnel (« 2MO », « -1FR ») ; Ordinal = 0 → tous.</summary>
public readonly record struct JourSemaineOrdinal(int Ordinal, DayOfWeek Jour);

/// <summary>
/// RRULE (RFC 5545) — sous-ensemble supporté défini en D-003, tout le reste rejeté bruyamment.
/// Aucun format de récurrence maison (règle 6).
/// </summary>
public sealed class RegleRecurrence
{
    public FrequenceRecurrence Frequence { get; private init; }
    public int Intervalle { get; private init; } = 1;
    public int? Nombre { get; private init; }                    // COUNT (DTSTART compte pour 1)
    public DateTimeOffset? JusquaInstantUtc { get; private init; } // UNTIL forme date-heure (borne incluse, comparée en UTC)
    public DateOnly? JusquaDateLocale { get; private init; }       // UNTIL forme date (borne incluse, comparée en date locale)
    public IReadOnlyList<int> JoursDuMois { get; private init; } = [];           // BYMONTHDAY (±1..31)
    public IReadOnlyList<JourSemaineOrdinal> JoursSemaine { get; private init; } = []; // BYDAY
    public IReadOnlyList<int> MoisDeLAnnee { get; private init; } = [];          // BYMONTH (1..12)
    public DayOfWeek DebutSemaine { get; private init; } = DayOfWeek.Monday;     // WKST (défaut MO)

    private RegleRecurrence() { }

    private static readonly Dictionary<string, DayOfWeek> JoursRfc = new(StringComparer.Ordinal)
    {
        ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday,
        ["TH"] = DayOfWeek.Thursday, ["FR"] = DayOfWeek.Friday, ["SA"] = DayOfWeek.Saturday,
        ["SU"] = DayOfWeek.Sunday,
    };

    public static RegleRecurrence Analyser(string rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule))
            throw new ErreurRecurrence("RRULE vide.");

        var texte = rrule.Trim();
        if (texte.StartsWith("RRULE:", StringComparison.Ordinal))
            texte = texte["RRULE:".Length..];

        FrequenceRecurrence? frequence = null;
        var intervalle = 1;
        int? nombre = null;
        DateTimeOffset? jusquaInstant = null;
        DateOnly? jusquaDate = null;
        List<int> joursDuMois = [];
        List<JourSemaineOrdinal> joursSemaine = [];
        List<int> moisDeLAnnee = [];
        var debutSemaine = DayOfWeek.Monday;
        var clesVues = new HashSet<string>(StringComparer.Ordinal);

        foreach (var partie in texte.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var egal = partie.IndexOf('=');
            if (egal <= 0 || egal == partie.Length - 1)
                throw new ErreurRecurrence($"Partie RRULE invalide : « {partie} ».");
            var cle = partie[..egal].ToUpperInvariant();
            var valeur = partie[(egal + 1)..];
            if (!clesVues.Add(cle))
                throw new ErreurRecurrence($"Partie RRULE dupliquée : « {cle} ».");

            switch (cle)
            {
                case "FREQ":
                    frequence = valeur.ToUpperInvariant() switch
                    {
                        "DAILY" => FrequenceRecurrence.Quotidienne,
                        "WEEKLY" => FrequenceRecurrence.Hebdomadaire,
                        "MONTHLY" => FrequenceRecurrence.Mensuelle,
                        "YEARLY" => FrequenceRecurrence.Annuelle,
                        "SECONDLY" or "MINUTELY" or "HOURLY" =>
                            throw new ErreurRecurrence($"FREQ={valeur} non supporté (D-003)."),
                        _ => throw new ErreurRecurrence($"FREQ inconnu : « {valeur} »."),
                    };
                    break;

                case "INTERVAL":
                    intervalle = EntierPositif(valeur, "INTERVAL");
                    break;

                case "COUNT":
                    nombre = EntierPositif(valeur, "COUNT");
                    break;

                case "UNTIL":
                    (jusquaInstant, jusquaDate) = AnalyserUntil(valeur);
                    break;

                case "BYMONTHDAY":
                    foreach (var v in valeur.Split(','))
                    {
                        if (!int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var jour)
                            || jour == 0 || Math.Abs(jour) > 31)
                            throw new ErreurRecurrence($"BYMONTHDAY invalide : « {v} » (±1..31).");
                        joursDuMois.Add(jour);
                    }
                    break;

                case "BYDAY":
                    foreach (var v in valeur.Split(','))
                        joursSemaine.Add(AnalyserJour(v));
                    break;

                case "BYMONTH":
                    foreach (var v in valeur.Split(','))
                    {
                        if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var mois)
                            || mois is < 1 or > 12)
                            throw new ErreurRecurrence($"BYMONTH invalide : « {v} » (1..12).");
                        moisDeLAnnee.Add(mois);
                    }
                    break;

                case "WKST":
                    if (!JoursRfc.TryGetValue(valeur.ToUpperInvariant(), out debutSemaine))
                        throw new ErreurRecurrence($"WKST invalide : « {valeur} ».");
                    break;

                case "BYSETPOS" or "BYWEEKNO" or "BYYEARDAY" or "BYHOUR" or "BYMINUTE" or "BYSECOND" or "RSCALE":
                    throw new ErreurRecurrence($"Partie RRULE non supportée : « {cle} » (D-003).");

                default:
                    throw new ErreurRecurrence($"Partie RRULE inconnue : « {cle} ».");
            }
        }

        if (frequence is null)
            throw new ErreurRecurrence("FREQ obligatoire.");
        if (nombre is not null && (jusquaInstant is not null || jusquaDate is not null))
            throw new ErreurRecurrence("COUNT et UNTIL sont mutuellement exclusifs (RFC 5545).");

        // Combinaisons hors sous-ensemble (D-003) — rejet bruyant plutôt qu'à-peu-près.
        switch (frequence.Value)
        {
            case FrequenceRecurrence.Quotidienne:
                if (joursDuMois.Count > 0 || joursSemaine.Count > 0 || moisDeLAnnee.Count > 0)
                    throw new ErreurRecurrence("BYMONTHDAY/BYDAY/BYMONTH non supportés avec FREQ=DAILY (D-003).");
                break;
            case FrequenceRecurrence.Hebdomadaire:
                if (joursDuMois.Count > 0 || moisDeLAnnee.Count > 0)
                    throw new ErreurRecurrence("BYMONTHDAY/BYMONTH non supportés avec FREQ=WEEKLY (D-003).");
                if (joursSemaine.Any(j => j.Ordinal != 0))
                    throw new ErreurRecurrence("BYDAY ordinal (ex. 2MO) non supporté avec FREQ=WEEKLY.");
                break;
            case FrequenceRecurrence.Mensuelle:
                if (moisDeLAnnee.Count > 0)
                    throw new ErreurRecurrence("BYMONTH non supporté avec FREQ=MONTHLY (D-003).");
                if (joursDuMois.Count > 0 && joursSemaine.Count > 0)
                    throw new ErreurRecurrence("BYMONTHDAY et BYDAY simultanés non supportés avec FREQ=MONTHLY (D-003).");
                break;
            case FrequenceRecurrence.Annuelle:
                if (joursSemaine.Count > 0)
                    throw new ErreurRecurrence("BYDAY non supporté avec FREQ=YEARLY (D-003).");
                break;
        }

        return new RegleRecurrence
        {
            Frequence = frequence.Value,
            Intervalle = intervalle,
            Nombre = nombre,
            JusquaInstantUtc = jusquaInstant,
            JusquaDateLocale = jusquaDate,
            JoursDuMois = joursDuMois,
            JoursSemaine = joursSemaine,
            MoisDeLAnnee = moisDeLAnnee,
            DebutSemaine = debutSemaine,
        };
    }

    private static int EntierPositif(string valeur, string cle)
    {
        if (!int.TryParse(valeur, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
            throw new ErreurRecurrence($"{cle} invalide : « {valeur} » (entier ≥ 1).");
        return n;
    }

    private static (DateTimeOffset?, DateOnly?) AnalyserUntil(string valeur)
    {
        // Forme date-heure UTC : AAAAMMJJ'T'HHMMSS'Z' — forme date : AAAAMMJJ.
        if (valeur.Length == 16 && valeur[8] == 'T' && valeur[15] == 'Z')
        {
            if (DateTime.TryParseExact(valeur, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant))
                return (new DateTimeOffset(instant, TimeSpan.Zero), null);
        }
        else if (valeur.Length == 8)
        {
            if (DateOnly.TryParseExact(valeur, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                return (null, date);
        }
        else if (valeur.Length == 15 && valeur[8] == 'T')
        {
            throw new ErreurRecurrence($"UNTIL sans « Z » : « {valeur} » — la forme date-heure doit être UTC (RFC 5545).");
        }

        throw new ErreurRecurrence($"UNTIL invalide : « {valeur} » (attendu AAAAMMJJ ou AAAAMMJJTHHMMSSZ).");
    }

    private static JourSemaineOrdinal AnalyserJour(string valeur)
    {
        var v = valeur.ToUpperInvariant();
        if (v.Length < 2)
            throw new ErreurRecurrence($"BYDAY invalide : « {valeur} ».");
        var codeJour = v[^2..];
        if (!JoursRfc.TryGetValue(codeJour, out var jour))
            throw new ErreurRecurrence($"BYDAY invalide : « {valeur} ».");
        var prefixe = v[..^2];
        if (prefixe.Length == 0)
            return new JourSemaineOrdinal(0, jour);
        if (!int.TryParse(prefixe, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ordinal)
            || ordinal == 0 || Math.Abs(ordinal) > 5)
            throw new ErreurRecurrence($"BYDAY ordinal invalide : « {valeur} » (±1..5).");
        return new JourSemaineOrdinal(ordinal, jour);
    }
}
