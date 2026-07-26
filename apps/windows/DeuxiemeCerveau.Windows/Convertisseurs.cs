using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Presentation.VueModeles;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace DeuxiemeCerveau.Windows.Convertisseurs;

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

/// <summary>
/// Masque quand la condition est VRAIE. Un convertisseur dédié plutôt qu'un paramètre : x:Bind ne
/// transmet pas de ConverterParameter aussi simplement que {Binding}, et un « inverse » implicite
/// se lit mal sur la ligne d'à côté.
/// </summary>
public sealed class BoolVersVisibiliteInverse : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Inverse un booléen — pour désactiver un contrôle pendant une opération en cours.</summary>
public sealed class BoolInverse : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) => value is not true;

    public object ConvertBack(object value, Type t, object p, string l) => value is not true;
}

public sealed class TexteVersVisibilite : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// Visible quand l'objet existe. Distinct de <see cref="TexteVersVisibilite"/>, qui ne juge que
/// des chaînes : lui passer un objet le rendrait toujours invisible, en silence.
/// </summary>
public sealed class ObjetVersVisibilite : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// Fond du verdict de confrontation (§5.1bis) : vert quand ça passe, rouge quand ça casse.
/// La couleur double le texte, elle ne le remplace pas — le verdict reste lisible sans elle.
/// </summary>
public sealed class FondVerdict : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true
            ? new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0xE8, 0xF0, 0xEA))   // SaugeDouce
            : new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0xF3, 0xE5, 0xE1));  // NegatifDouce

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Encre du verdict, contrastée sur le fond ci-dessus.</summary>
public sealed class EncreVerdict : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true
            ? new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x36, 0x69, 0x4A))   // sauge assombrie, AA sur SaugeDouce
            : new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x9A, 0x3E, 0x2E));  // terre cuite assombrie

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// <c>bool</c> ↔ <c>bool?</c> pour <c>CheckBox.IsChecked</c>. Sans lui, un <c>x:Bind</c> TwoWay
/// depuis un <c>bool</c> à travers un chemin imbriqué laisse la case en état INDÉTERMINÉ — un rond
/// barré au lieu d'une coche, sans la moindre erreur pour le signaler.
/// </summary>
public sealed class BoolCoche : IValueConverter
{
    public object? Convert(object value, Type t, object p, string l) => value is true;

    public object ConvertBack(object value, Type t, object p, string l) => value is true;
}

/// <summary>Nom lisible d'un type d'Élément (§3.1). « Rendezvous » ne se montre pas tel quel.</summary>
public sealed class NomType : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) => value switch
    {
        TypeElement.Facture => "Facture",
        TypeElement.Paiement => "Paiement",
        TypeElement.Revenu => "Revenu",
        TypeElement.Rendezvous => "Rendez-vous",
        TypeElement.Envie => "Envie",
        TypeElement.Tache => "Tâche",
        TypeElement.Note => "Note",
        _ => "",
    };

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// <see cref="DateOnly"/> ↔ <see cref="DateTimeOffset"/> pour CalendarDatePicker, qui ne connaît
/// pas DateOnly. Le fuseau de l'Élément est posé plus tard, à l'enregistrement (§3.5) : ici on ne
/// manipule qu'un jour de calendrier, sans heure.
/// </summary>
public sealed class JourDate : IValueConverter
{
    public object? Convert(object value, Type t, object p, string l) =>
        value is DateOnly jour
            ? new DateTimeOffset(jour.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

    public object ConvertBack(object value, Type t, object p, string l) =>
        value is DateTimeOffset instant
            ? DateOnly.FromDateTime(instant.DateTime)
            : DateOnly.FromDateTime(DateTime.Now);
}

/// <summary>
/// <see cref="Periodicite"/> ↔ index de ComboBox. L'ordre des items du XAML suit celui de l'enum ;
/// les deux se lisent l'un à côté de l'autre, ce qui rend l'écart visible s'il apparaît.
/// </summary>
public sealed class IndexPeriodicite : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is Periodicite periodicite ? (int)periodicite : 0;

    public object ConvertBack(object value, Type t, object p, string l) =>
        value is int index && Enum.IsDefined(typeof(Periodicite), index)
            ? (Periodicite)index
            : Periodicite.Aucune;
}

/// <summary>Visible quand l'objet est ABSENT — l'état vide en regard du précédent.</summary>
public sealed class ObjetAbsentVersVisibilite : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

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

/// <summary>
/// Un montant d'entrée s'affiche en sauge ; une sortie garde l'encre normale. Accepte le
/// <see cref="Sens"/> comme un booléen « est une entrée » : les deux formes existent selon que la
/// vue tient l'Élément ou une ligne déjà mise en forme.
/// </summary>
public sealed class CouleurMontant : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is Sens.Entree or true ? Pinceaux.Par("Positif") : Pinceaux.Par("Encre");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// Pastille de statut : sauge une fois réglé, ocre tant que ça reste dû. Le libellé change en
/// même temps (« Payé » / « À valider ») — la couleur n'est jamais le seul indice.
/// </summary>
public sealed class FondStatut : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Pinceaux.Par("SaugeDouce") : Pinceaux.Par("OcreDouce");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Encre de la pastille de statut, assortie à son fond.</summary>
public sealed class EncreStatut : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Pinceaux.Par("Sauge") : Pinceaux.Par("Ocre");

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

/// <summary>Fond d'un mois à découvert : la seule alarme visuelle de l'app (§5.1, mise en évidence).</summary>
public sealed class FondDecouvert : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Pinceaux.Par("NegatifDouce") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Encre d'un solde de clôture : terre cuite s'il est négatif.</summary>
public sealed class EncreDecouvert : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Pinceaux.Par("Negatif") : Pinceaux.Par("Encre");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// Fond doux d'une pastille de calendrier, assorti à la couleur de type (sauge/terre cuite/…).
/// La maquette pose la pastille sur son propre fond teinté, pas sur du blanc.
/// </summary>
public sealed class FondType : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) => value switch
    {
        TypeElement.Revenu => Pinceaux.Par("SaugeDouce"),
        TypeElement.Facture or TypeElement.Paiement => Pinceaux.Par("TerreCuiteDouce"),
        TypeElement.Rendezvous => Pinceaux.Par("ArdoiseDouce"),
        TypeElement.Tache => Pinceaux.Par("OcreDouce"),
        _ => Pinceaux.Par("MauveDouce"),
    };

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// Intitulé du jour dans la vue « 7 prochains jours » : bleu de marque pour aujourd'hui, encre
/// ordinaire ensuite. Seul usage du bleu hors des actions, comme la pastille du jour dans la grille.
/// </summary>
public sealed class EncreJourCourant : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Pinceaux.Par("MarqueEncre") : Pinceaux.Par("Encre3");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Encre d'un filtre de calendrier : pleine s'il est affiché, pâle s'il est masqué.</summary>
public sealed class EncreFiltre : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Pinceaux.Par("Encre") : Pinceaux.Par("Encre3");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// Pastille d'un filtre masqué : presque effacée. La maquette la montre creuse ; l'opacité donne
/// le même signal sans dupliquer la forme, et le libellé pâlit en même temps — la couleur n'est
/// donc jamais le seul indice de l'état.
/// </summary>
public sealed class OpaciteFiltre : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) => value is true ? 1.0 : 0.25;

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>
/// Encre du numéro de jour. Hors du mois affiché, c'est du contexte adjacent qu'on estompe
/// délibérément (Encre4), pas du texte à lire — d'où le seul usage de l'encre la plus pâle.
/// </summary>
public sealed class EncreJour : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Pinceaux.Par("Encre4") : Pinceaux.Par("Encre2");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}

/// <summary>Un solde négatif ressort en terre cuite — la seule alarme visuelle de l'app.</summary>
public sealed class CouleurSolde : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is long c && c < 0 ? Pinceaux.Par("Negatif") : Pinceaux.Par("Positif");

    public object ConvertBack(object value, Type t, object p, string l) => throw new NotSupportedException();
}
