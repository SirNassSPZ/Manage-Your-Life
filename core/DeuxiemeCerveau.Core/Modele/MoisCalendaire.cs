using System.Globalization;

namespace DeuxiemeCerveau.Core.Modele;

/// <summary>Mois calendaire (année + mois), unité de la cascade du budget projeté (§5.1).</summary>
public readonly record struct MoisCalendaire : IComparable<MoisCalendaire>
{
    public int Annee { get; }

    /// <summary>1 à 12.</summary>
    public int Mois { get; }

    public MoisCalendaire(int annee, int mois)
    {
        if (mois is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(mois), mois, "Le mois doit être entre 1 et 12.");
        Annee = annee;
        Mois = mois;
    }

    /// <summary>Index absolu (mois écoulés depuis l'an 0) — support des comparaisons et de l'arithmétique.</summary>
    public int Index => Annee * 12 + (Mois - 1);

    public MoisCalendaire AjouterMois(int n)
    {
        var index = Index + n;
        var annee = index >= 0 ? index / 12 : (index - 11) / 12; // division plancher
        return new MoisCalendaire(annee, index - annee * 12 + 1);
    }

    public static MoisCalendaire Depuis(DateOnly date) => new(date.Year, date.Month);

    public static MoisCalendaire Depuis(DateTime local) => new(local.Year, local.Month);

    public int CompareTo(MoisCalendaire autre) => Index.CompareTo(autre.Index);

    public static bool operator <(MoisCalendaire a, MoisCalendaire b) => a.Index < b.Index;
    public static bool operator >(MoisCalendaire a, MoisCalendaire b) => a.Index > b.Index;
    public static bool operator <=(MoisCalendaire a, MoisCalendaire b) => a.Index <= b.Index;
    public static bool operator >=(MoisCalendaire a, MoisCalendaire b) => a.Index >= b.Index;

    /// <summary>Format « AAAA-MM » (golden files).</summary>
    public override string ToString() => $"{Annee:D4}-{Mois:D2}";

    public static MoisCalendaire Analyser(string texte)
    {
        var parties = texte.Split('-');
        if (parties.Length != 2
            || !int.TryParse(parties[0], NumberStyles.None, CultureInfo.InvariantCulture, out var annee)
            || !int.TryParse(parties[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mois))
            throw new FormatException($"Mois calendaire invalide : « {texte} » (attendu AAAA-MM).");
        return new MoisCalendaire(annee, mois);
    }
}
