namespace DeuxiemeCerveau.Presentation;

/// <summary>
/// Choix d'un fichier par l'utilisateur. Abstrait parce que l'implémentation exige Windows —
/// <c>FileSavePicker</c> réclame le HWND de la fenêtre en application non empaquetée — alors que
/// tout ce qui l'entoure (quoi exporter, quand refuser un import) doit rester testable (D-022).
/// <para>
/// Renvoie un flux plutôt qu'un chemin : c'est le contrat de <c>ServiceExport.Exporter(Stream)</c>,
/// et cela laisse un test brancher un <see cref="MemoryStream"/> sans toucher au disque.
/// </para>
/// </summary>
public interface ISelecteurFichier
{
    /// <summary>Flux d'écriture pour l'archive, ou null si l'utilisateur a renoncé.</summary>
    Task<Stream?> PourEcrire(string nomPropose);

    /// <summary>Flux de lecture d'une archive existante, ou null si l'utilisateur a renoncé.</summary>
    Task<Stream?> PourLire();
}
