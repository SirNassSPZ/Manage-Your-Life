using DeuxiemeCerveau.Core.Modele;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace DeuxiemeCerveau.Windows.Presentation;

/// <summary>Ressource pinceau du dictionnaire de jetons, par clé.</summary>
internal static class Pinceaux
{
    public static Brush Par(string cle) => (Brush)Application.Current.Resources[cle];
}

/// <summary>Fond d'une entrée de navigation active (bleu de marque doux) ou inactive (transparent).</summary>
public sealed class FondActif : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Pinceaux.Par("MarqueDouce") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Encre d'une entrée de navigation : bleu si active, gris sinon, très pâle si V2.</summary>
public sealed class EncreActif : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Pinceaux.Par("MarqueEncre") : Pinceaux.Par("Encre2");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

public sealed class BoolVersVisibilite : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var vrai = value is true;
        if (p as string == "inverse") vrai = !vrai;
        return vrai ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

public sealed class TexteVersVisibilite : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// Pastille de couleur d'une occurrence. La palette est fonctionnelle, jamais décorative :
/// sauge = revenus, terre cuite = sorties, ardoise = travail/rendez-vous, ocre = tâches.
/// </summary>
public sealed class CouleurType : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) => value switch
    {
        TypeElement.Revenu => Pinceaux.Par("Sauge"),
        TypeElement.Facture or TypeElement.Paiement => Pinceaux.Par("TerreCuite"),
        TypeElement.Rendezvous => Pinceaux.Par("Ardoise"),
        TypeElement.Tache => Pinceaux.Par("Ocre"),
        _ => Pinceaux.Par("Mauve"),
    };

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Un montant d'entrée s'affiche en sauge ; une sortie garde l'encre normale.</summary>
public sealed class CouleurMontant : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is Sens.Entree ? Pinceaux.Par("Positif") : Pinceaux.Par("Encre");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// Tracé SVG (chaîne) vers <see cref="Geometry"/>. Nécessaire parce qu'une liaison ne déclenche
/// pas la conversion implicite dont bénéficie un littéral XAML.
/// </summary>
public sealed class TraceGeometrie : IValueConverter
{
    public object? Convert(object value, Type t, object p, string l) =>
        value is string trace && trace.Length > 0
            ? (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), trace)
            : null;

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Couleur « #RRGGBB » du modèle vers pinceau (les catégories portent leur couleur, §3.3).</summary>
public sealed class HexPinceau : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        if (value is not string hex || hex.Length != 7 || hex[0] != '#') return Pinceaux.Par("Mauve");

        // « global:: » obligatoire : dans le namespace DeuxiemeCerveau.Windows, un « Windows. »
        // nu se résout sur notre propre racine, pas sur le SDK.
        return new SolidColorBrush(global::Windows.UI.Color.FromArgb(
            255,
            System.Convert.ToByte(hex.Substring(1, 2), 16),
            System.Convert.ToByte(hex.Substring(3, 2), 16),
            System.Convert.ToByte(hex.Substring(5, 2), 16)));
    }

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Un solde négatif ressort en terre cuite — la seule alarme visuelle de l'app.</summary>
public sealed class CouleurSolde : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is long c && c < 0 ? Pinceaux.Par("Negatif") : Pinceaux.Par("Positif");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}
