using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DeuxiemeCerveau.Windows.Outils;

/// <summary>
/// Capture l'arbre visuel dans un PNG, pour comparer le rendu réel à docs/maquette.html.
/// <para>
/// Passe par <see cref="RenderTargetBitmap"/> et non par une copie d'écran : WinUI 3 compose son
/// contenu via DirectComposition, que ni <c>CopyFromScreen</c> ni <c>PrintWindow</c> ne savent
/// lire (l'un capture la fenêtre du dessous, l'autre rend une image noire).
/// </para>
/// </summary>
public static class CaptureVisuel
{
    /// <summary>Option de ligne de commande : <c>--capture &lt;chemin.png&gt;</c>.</summary>
    public const string Option = "--capture";

    public static string? CheminDemande(string[] arguments)
    {
        var i = Array.IndexOf(arguments, Option);
        return i >= 0 && i + 1 < arguments.Length ? arguments[i + 1] : null;
    }

    public static async Task Enregistrer(UIElement element, string chemin)
    {
        var rendu = new RenderTargetBitmap();
        await rendu.RenderAsync(element);
        var tampon = await rendu.GetPixelsAsync();

        // DataReader.FromBuffer plutôt que IBuffer.ToArray() : l'extension vit dans une projection
        // WinRT qui n'est pas référencée ici.
        var pixels = new byte[tampon.Length];
        DataReader.FromBuffer(tampon).ReadBytes(pixels);

        var flux = new InMemoryRandomAccessStream();
        var encodeur = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, flux);
        encodeur.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)rendu.PixelWidth,
            (uint)rendu.PixelHeight,
            96, 96,
            pixels);
        await encodeur.FlushAsync();

        flux.Seek(0);
        var octets = new byte[flux.Size];
        using (var lecteur = new DataReader(flux.GetInputStreamAt(0)))
        {
            await lecteur.LoadAsync((uint)flux.Size);
            lecteur.ReadBytes(octets);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        await File.WriteAllBytesAsync(chemin, octets);
    }
}
