using System.IO.Compression;
using System.Text.Json;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Donnees;

/// <summary>
/// Import d'une archive d'export (§5.7) dans une installation VIERGE (V1 : import dans une base vide,
/// pas de fusion). Reconstitue l'état complet — corbeille comprise — depuis <c>donnees.json</c>, en
/// local et sans réseau. Application atomique (tout ou rien).
/// </summary>
public sealed class ServiceImport(DepotLocal depot)
{
    public void Importer(Stream entree)
    {
        using var zip = new ZipArchive(entree, ZipArchiveMode.Read);
        var donnees = zip.GetEntry(ServiceExport.FichierDonnees)
            ?? throw new ErreurImport($"Archive invalide : « {ServiceExport.FichierDonnees} » absent.");

        string json;
        using (var lecteur = new StreamReader(donnees.Open()))
            json = lecteur.ReadToEnd();
        var archive = SerialisationCanonique.Deserialiser<ArchiveDonnees>(json);

        depot.DansTransaction(() =>
        {
            Ecrire(EntiteSynchro.Element, archive.Elements);
            Ecrire(EntiteSynchro.Categorie, archive.Categories);
            Ecrire(EntiteSynchro.Projet, archive.Projets);
            Ecrire(EntiteSynchro.Budget, archive.Budgets);
            Ecrire(EntiteSynchro.PieceJointe, archive.PiecesJointes);
            if (archive.Reglage is { } reglage)
                depot.EcrirePayload(EntiteSynchro.Reglage, reglage.GetRawText());
        });
    }

    private void Ecrire(EntiteSynchro type, IReadOnlyList<JsonElement> entites)
    {
        foreach (var e in entites)
            depot.EcrirePayload(type, e.GetRawText());
    }
}

/// <summary>Archive d'import illisible ou incomplète (§5.7).</summary>
public sealed class ErreurImport(string message) : Exception(message);
