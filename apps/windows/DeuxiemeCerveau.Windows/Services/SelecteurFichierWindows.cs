using DeuxiemeCerveau.Presentation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DeuxiemeCerveau.Windows.Services;

/// <summary>
/// Boîtes de dialogue de fichier. Vit ici et pas dans la présentation pour une raison technique
/// nette : en application <b>non empaquetée</b>, un <c>FileSavePicker</c> n'a pas de fenêtre parente
/// implicite et lève <c>E_INVALIDARG</c> tant qu'on ne lui a pas donné le HWND à la main
/// (<c>InitializeWithWindow</c>). Le reste — quoi exporter, quand refuser un import — est dans
/// <see cref="DeuxiemeCerveau.Presentation.VueModeles.VueModeleSauvegarde"/>, donc testé (D-022).
/// </summary>
internal sealed class SelecteurFichierWindows(Func<IntPtr> fenetre) : ISelecteurFichier
{
    private const string Archive = ".zip";

    public async Task<Stream?> PourEcrire(string nomPropose)
    {
        var selecteur = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = nomPropose,
        };
        selecteur.FileTypeChoices.Add("Archive Deuxième Cerveau", [Archive]);
        Ancrer(selecteur);

        if (await selecteur.PickSaveFileAsync() is not { } fichier) return null;

        // File.Create plutôt que les extensions de flux WinRT : celles-ci
        // (OpenStreamForWriteAsync) ont disparu du .NET moderne. Et Create TRONQUE — le sélecteur
        // peut rendre un fichier existant, dont une archive plus courte laisserait la queue.
        return File.Create(CheminDe(fichier));
    }

    public async Task<Stream?> PourLire()
    {
        var selecteur = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        selecteur.FileTypeFilter.Add(Archive);
        Ancrer(selecteur);

        return await selecteur.PickSingleFileAsync() is { } fichier
            ? File.OpenRead(CheminDe(fichier))
            : null;
    }

    private void Ancrer(object selecteur) =>
        WinRT.Interop.InitializeWithWindow.Initialize(selecteur, fenetre());

    /// <summary>
    /// Un fichier servi par un fournisseur virtuel (OneDrive à la demande, MTP) n'a pas de chemin
    /// sur disque. Le dire franchement vaut mieux qu'un <c>ArgumentException</c> nu remonté à
    /// l'écran par le modèle de vue.
    /// </summary>
    private static string CheminDe(StorageFile fichier) =>
        string.IsNullOrEmpty(fichier.Path)
            ? throw new IOException(
                "Cet emplacement n'expose pas de fichier sur disque. Choisissez un dossier local.")
            : fichier.Path;
}
