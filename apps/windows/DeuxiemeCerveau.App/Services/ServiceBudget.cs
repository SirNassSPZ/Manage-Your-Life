using System.Globalization;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Modele;

namespace DeuxiemeCerveau.App.Services;

/// <summary>Un mois de la projection, tel que l'app l'affiche (§5.1) — reçu du serveur, jamais recalculé.</summary>
public sealed record MoisBudget(
    string Mois,
    long? OuvertureCentimes,
    long EntreesCentimes,
    long SortiesCentimes,
    long? ClotureCentimes,
    bool Decouvert,
    bool AvantReference);

/// <summary>
/// Le prochain mois dans le rouge, <b>avec de quoi agir</b> (reco #6) : une alerte qui n'annonce pas
/// seulement un découvert, mais montre les plus grosses sorties du mois — de quoi décider quoi décaler.
/// </summary>
public sealed record AlerteBudget(
    string Mois,
    long ClotureCentimes,
    IReadOnlyList<OccurrenceCalendrier> PlusGrossesSorties);

/// <summary>La vue « Budget projeté » complète : l'horizon mois par mois + la prochaine alerte.</summary>
public sealed record VueBudget(IReadOnlyList<MoisBudget> Mois, AlerteBudget? ProchaineAlerte);

/// <summary>
/// Vue « Budget projeté » (feuille de route Palier 1 rang 5 / recos #5 et #6) — Étape 4f.
///
/// <para><b>Frontière §4 tenue.</b> L'algorithme du budget projeté (§5.1) « vit dans l'API et uniquement
/// là » : ce service <b>lit</b> la projection via <see cref="IClientApi.Projeter"/> et ne refait aucun
/// calcul de cascade. Sa valeur ajoutée est de <b>rendre lisible</b> (horizon + mois rouges) et
/// d'<b>ouvrir sur l'action</b> — pour un mois en découvert, il désigne les plus grosses sorties de ce
/// mois, lues dans les données <b>locales</b> (occurrences développées pour l'affichage, §4).</para>
///
/// Rien n'est stocké : tout est calculé à la lecture (règle 9).
/// </summary>
public sealed class ServiceBudget(IClientApi api, ServiceCalendrier calendrier)
{
    /// <summary>
    /// Charge l'horizon (<paramref name="mois"/> mois glissants, §5.1) et prépare la prochaine alerte.
    /// <paramref name="sortiesEnVedette"/> : combien de sorties montrer pour le mois rouge (reco #6).
    /// </summary>
    public async Task<VueBudget> Charger(int mois = 6, int sortiesEnVedette = 3, CancellationToken jeton = default)
    {
        if (mois < 1)
            mois = 1;
        var projection = await api.Projeter(mois, jeton);

        var lignes = projection.Mois
            .Select(m => new MoisBudget(
                m.Mois, m.OuvertureCentimes, m.EntreesCentimes, m.SortiesCentimes,
                m.ClotureCentimes, m.Decouvert, m.AvantReference))
            .ToList();

        // Le premier mois dans le rouge : c'est LUI qu'on met en avant (un seul point focal, reco #5).
        var rouge = lignes.FirstOrDefault(m => m.Decouvert);
        var alerte = rouge is null
            ? null
            : new AlerteBudget(rouge.Mois, rouge.ClotureCentimes ?? 0, SortiesDuMois(rouge.Mois, sortiesEnVedette));

        return new VueBudget(lignes, alerte);
    }

    /// <summary>
    /// Les plus grosses sorties d'un mois (« aaaa-MM »), lues en local — le levier d'action de l'alerte.
    /// Une même série récurrente peut donc apparaître par son occurrence de ce mois-là, ce qui est voulu.
    /// </summary>
    public IReadOnlyList<OccurrenceCalendrier> SortiesDuMois(string mois, int combien = 3)
    {
        if (combien < 1 || !TryFenetre(mois, out var debut, out var fin))
            return [];
        return calendrier.Occurrences(debut, fin)
            .Where(o => o.Sens == Sens.Sortie && o.MontantCentimes is > 0)
            .OrderByDescending(o => o.MontantCentimes!.Value)
            .ThenBy(o => o.InstantUtc)
            .Take(combien)
            .ToList();
    }

    /// <summary>Fenêtre [1er du mois, dernier instant du mois] pour un mois « aaaa-MM ».</summary>
    private static bool TryFenetre(string mois, out DateTimeOffset debut, out DateTimeOffset fin)
    {
        debut = default;
        fin = default;
        if (!DateTime.TryParseExact(mois, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var premier))
            return false;
        debut = new DateTimeOffset(premier.Year, premier.Month, 1, 0, 0, 0, TimeSpan.Zero);
        fin = debut.AddMonths(1).AddTicks(-1);
        return true;
    }
}
