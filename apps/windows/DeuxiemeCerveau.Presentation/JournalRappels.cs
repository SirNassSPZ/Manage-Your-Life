using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeuxiemeCerveau.Presentation;

/// <summary>
/// Ce que cet appareil a déjà notifié, et ce que l'utilisateur y a coupé.
/// <para>
/// Un fichier JSON à côté de la base, <b>délibérément hors du schéma synchronisé</b> : le §4 confie
/// les rappels à chaque appareil (« chaque appareil notifie »). Ce qui a sonné ici ne regarde pas
/// les autres appareils, et la règle 18 n'a pas à porter une table pour ça.
/// </para>
/// <para>
/// Sans cette mémoire, un déclenchement en double — la tâche planifiée puis une ouverture de
/// session — reposterait les mêmes toasts. Deux notifications identiques dans la journée, et
/// l'utilisateur apprend à toutes les ignorer.
/// </para>
/// </summary>
public sealed class JournalRappels
{
    /// <summary>Au-delà, une clé datée ne peut plus se représenter : elle ne sert qu'à grossir.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _chemin;
    private readonly Dictionary<string, DateTimeOffset> _remis;

    private JournalRappels(string chemin, Etat etat)
    {
        _chemin = chemin;
        _remis = etat.Remis is null
            ? []
            : new Dictionary<string, DateTimeOffset>(etat.Remis, StringComparer.Ordinal);
        RappelExportActif = etat.RappelExportActif;
    }

    /// <summary>
    /// Rappel mensuel d'export : <b>désactivable</b>, comme l'exige le §5.7. Actif par défaut —
    /// c'est une protection, elle doit être là sans qu'on la demande.
    /// </summary>
    public bool RappelExportActif { get; set; }

    /// <summary>Nom du fichier, à côté de <c>local.db</c>.</summary>
    public const string NomFichier = "rappels.json";

    /// <summary>
    /// Lit le journal de <paramref name="dossier"/> (défaut : <see cref="Composition.DossierParDefaut"/>).
    /// Un fichier absent ou illisible donne un journal vide : perdre la mémoire des rappels fait au
    /// pire une notification en double, jamais une panne au démarrage.
    /// </summary>
    public static JournalRappels Ouvrir(string? dossier = null)
    {
        var chemin = Path.Combine(dossier ?? Composition.DossierParDefaut, NomFichier);
        try
        {
            if (File.Exists(chemin)
                && JsonSerializer.Deserialize<Etat>(File.ReadAllText(chemin), Format) is { } etat)
                return new JournalRappels(chemin, etat);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Journal illisible : on repart d'un journal neuf plutôt que d'empêcher la notification.
        }

        return new JournalRappels(chemin, new Etat());
    }

    /// <summary>Ceux qui n'ont pas encore été remis — dans l'ordre reçu.</summary>
    public IReadOnlyList<Rappel> Inedits(IEnumerable<Rappel> rappels) =>
        [.. rappels.Where(r => !_remis.ContainsKey(r.Cle))];

    /// <summary>
    /// Note un rappel comme remis. Appelé <b>après</b> la remise, jamais avant : un toast qui
    /// échoue doit pouvoir repasser au déclenchement suivant.
    /// </summary>
    public void Noter(Rappel rappel, DateTimeOffset maintenant) => _remis[rappel.Cle] = maintenant;

    /// <summary>
    /// Écrit le journal sur disque, purgé de ce qui a passé <see cref="Retention"/>.
    /// Échoue en silence : ne pas savoir se souvenir ne doit pas faire tomber la tâche planifiée.
    /// </summary>
    public void Enregistrer(DateTimeOffset maintenant)
    {
        var limite = maintenant - Retention;
        foreach (var perimee in _remis.Where(p => p.Value < limite).Select(p => p.Key).ToList())
            _remis.Remove(perimee);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_chemin)!);
            File.WriteAllText(
                _chemin,
                JsonSerializer.Serialize(new Etat(_remis, RappelExportActif), Format));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Voir Ouvrir : au pire une notification en double au prochain déclenchement.
        }
    }

    /// <summary>Forme sur disque. Nommée pour être lisible à l'œil nu dans le fichier.</summary>
    private sealed record Etat(
        Dictionary<string, DateTimeOffset>? Remis = null,
        bool RappelExportActif = true);
}
