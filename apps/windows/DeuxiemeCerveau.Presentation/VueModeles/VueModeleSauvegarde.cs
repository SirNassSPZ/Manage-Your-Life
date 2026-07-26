using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeuxiemeCerveau.App.Donnees;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Presentation.VueModeles;

/// <summary>
/// Export et import de l'archive locale (§5.7). <b>Sans réseau, jamais</b> : c'est le point décisif
/// de la spec — un export qui dépendrait de l'API deviendrait inaccessible au moment précis où on en
/// a besoin.
/// <para>
/// Porte aussi le drapeau du rappel mensuel, que le §5.7 exige <b>désactivable</b>. Il vit dans
/// <see cref="JournalRappels"/>, hors du schéma synchronisé : notifier est une affaire d'appareil.
/// </para>
/// </summary>
public sealed partial class VueModeleSauvegarde : ObservableObject
{
    private readonly Composition _composition;
    private readonly ISelecteurFichier _selecteur;

    public VueModeleSauvegarde(Composition composition, ISelecteurFichier selecteur)
    {
        _composition = composition;
        _selecteur = selecteur;
        _rappelMensuel = JournalRappels.Ouvrir(_composition.Dossier).RappelExportActif;
    }

    /// <summary>Rappelé après un import réussi : tout l'écran est à relire.</summary>
    public Action? ApresImport { get; set; }

    /// <summary>Retour de la dernière opération — succès comme échec. Jamais silencieux.</summary>
    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _occupe;

    /// <summary>§5.7 : le rappel mensuel d'export est <b>désactivable</b>.</summary>
    [ObservableProperty]
    private bool _rappelMensuel;

    partial void OnRappelMensuelChanged(bool value)
    {
        var journal = JournalRappels.Ouvrir(_composition.Dossier);
        journal.RappelExportActif = value;
        journal.Enregistrer(DateTimeOffset.Now);
    }

    /// <summary>Nom proposé : daté, trié naturellement, lisible dans un dossier de sauvegardes.</summary>
    public static string NomPropose(DateTimeOffset maintenant) =>
        $"deuxieme-cerveau-{maintenant.LocalDateTime:yyyy-MM-dd}.zip";

    [RelayCommand]
    private async Task Exporter()
    {
        if (Occupe) return;
        Occupe = true;
        Message = null;
        try
        {
            if (await _selecteur.PourEcrire(NomPropose(DateTimeOffset.Now)) is not { } sortie)
                return;                                     // renoncé : pas un échec, pas un message

            using (sortie)
                _composition.Acces.Lire(() => _composition.Export.Exporter(sortie));

            Message = "Archive écrite. Rangez-la hors de l'application — disque externe, autre cloud.";
        }
        catch (Exception ex)
        {
            Message = "Export impossible : " + ex.Message;
        }
        finally { Occupe = false; }
    }

    /// <summary>
    /// Import d'une archive. <b>Refusé si l'installation n'est pas vierge</b> — la V1 ne fait pas de
    /// fusion (§5.7), et <c>ServiceImport</c> écrase entité par entité : lancé sur des données
    /// existantes, il produirait un mélange des deux états que rien ne saurait défaire.
    /// </summary>
    [RelayCommand]
    private async Task Importer()
    {
        if (Occupe) return;

        if (!EstVierge())
        {
            Message = "Import refusé : cette installation contient déjà des données. "
                    + "La V1 ne réimporte que dans une installation vierge (§5.7).";
            return;
        }

        Occupe = true;
        Message = null;
        try
        {
            if (await _selecteur.PourLire() is not { } entree)
                return;

            using (entree)
                _composition.Acces.Ecrire(() => _composition.Import.Importer(entree));

            Message = "Archive restaurée.";
            ApresImport?.Invoke();
        }
        catch (ErreurImport ex)
        {
            // Le motif est déjà écrit pour l'utilisateur : on le relaie tel quel.
            Message = ex.Message;
        }
        catch (Exception ex)
        {
            Message = "Import impossible : " + ex.Message;
        }
        finally { Occupe = false; }
    }

    /// <summary>
    /// Vierge = aucun Élément, aucune catégorie — corbeille comprise. On interroge le dépôt brut
    /// plutôt que les listes actives : une installation qui n'aurait que des Éléments supprimés
    /// n'est pas vierge, et un import y écraserait une corbeille encore récupérable (filet 2).
    /// </summary>
    public bool EstVierge() => _composition.Acces.Lire(() =>
        _composition.Depot.Enumerer(EntiteSynchro.Element).Count == 0
        && _composition.Depot.Enumerer(EntiteSynchro.Categorie).Count == 0);
}
