using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Recurrence;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.Core.Projection;

/// <summary>Requête de projection : le mois courant est fourni par l'appelant (API) — le cœur est pur et testable.</summary>
public sealed record RequeteProjection(
    MoisCalendaire PremierMois,
    int NombreMois,
    SoldeReference Solde,
    IReadOnlyList<Element> Elements);

/// <summary>Le solde de référence (§3.4) : sans point de départ, aucune projection n'est possible.</summary>
public sealed record SoldeReference(long Centimes, DateOnly Date);

/// <summary>
/// Un mois projeté : ouverture, entrées, sorties, clôture (§8). Soldes null quand le mois précède
/// la date de référence (avant_reference, D-004). Découvert = clôture négative (§5.1 point 5).
/// </summary>
public sealed record MoisProjete(
    int Annee,
    int Mois,
    long? OuvertureCentimes,
    long EntreesCentimes,
    long SortiesCentimes,
    long? ClotureCentimes,
    bool Decouvert,
    bool AvantReference);

/// <summary>
/// Confrontation d'un montant au budget projeté (§5.1bis) : « est-ce que ça rentre en septembre ? »
/// </summary>
/// <param name="Base">La projection nominale à confronter — l'envie n'y figure pas.</param>
/// <param name="MontantCentimes">Prix prêté à l'envie, strictement positif (règle 5).</param>
/// <param name="MoisCible">Le mois où l'on imagine la dépense. Doit être dans l'horizon.</param>
public sealed record RequeteConfrontation(
    RequeteProjection Base,
    long MontantCentimes,
    MoisCalendaire MoisCible);

/// <summary>
/// Réponse d'une confrontation (§5.1bis). Les deux cascades sont rendues pour que l'app montre
/// l'écart mois par mois ; le verdict seul ne dirait pas <b>de combien</b> ça coince.
/// </summary>
/// <param name="Passe">Vrai si aucun mois, de la cible à la fin de l'horizon, ne clôture négatif.</param>
/// <param name="PremierMoisQuiCasse">Le premier mois qui passe en négatif, ou null si ça passe.</param>
/// <param name="ManqueCentimes">Ce qui manque au pire moment. Zéro si ça passe.</param>
public sealed record Confrontation(
    IReadOnlyList<MoisProjete> Nominale,
    IReadOnlyList<MoisProjete> Simulee,
    bool Passe,
    MoisProjete? PremierMoisQuiCasse,
    long ManqueCentimes);

/// <summary>
/// Budget projeté — algorithme officiel (§5.1, NON NÉGOCIABLE). Vit dans l'API et uniquement ici ;
/// calculé à la lecture, jamais stocké (règle 9). Précisions d'implémentation : D-004.
/// </summary>
public static class CalculateurProjection
{
    public const int NombreMoisMax = 120;

    public static IReadOnlyList<MoisProjete> Calculer(RequeteProjection requete) =>
        Calculer(requete, sortieSupplementaire: null);

    /// <param name="sortieSupplementaire">
    /// Sortie hypothétique injectée dans la cascade, pour la confrontation (§5.1bis). Passée ici
    /// plutôt que sous forme d'Élément de synthèse : fabriquer un faux Élément demanderait de lui
    /// inventer une date et un fuseau, et le rattachement au mois local pourrait le déplacer d'un
    /// mois. Un montant posé directement sur le mois cible ne peut pas glisser.
    /// </param>
    private static IReadOnlyList<MoisProjete> Calculer(
        RequeteProjection requete, (MoisCalendaire Mois, long Centimes)? sortieSupplementaire)
    {
        if (requete.NombreMois is < 1 or > NombreMoisMax)
            throw new ArgumentOutOfRangeException(nameof(requete),
                $"Nombre de mois entre 1 et {NombreMoisMax}.");

        // Point de départ (§5.1.2) : le solde de référence à sa date — instant = ce jour à 00:00 UTC,
        // occurrences comptées à partir de cet instant inclus (D-004).
        var instantReference = new DateTimeOffset(
            requete.Solde.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var moisReference = MoisCalendaire.Depuis(requete.Solde.Date);
        var dernierMois = requete.PremierMois.AjouterMois(requete.NombreMois - 1);

        // Fenêtre d'expansion (§5.1.3) : de la date de référence à la fin de l'horizon.
        // Borne UTC large (les occurrences sont ensuite filtrées par mois local) : 1er jour du mois
        // suivant l'horizon + 2 jours de marge (tout offset réel est dans ±14 h).
        var finHorizonUtc = new DateTimeOffset(
            new DateTime(dernierMois.Annee, dernierMois.Mois, 1, 0, 0, 0, DateTimeKind.Utc))
            .AddMonths(1).AddDays(2);

        var flux = new Dictionary<MoisCalendaire, (long Entrees, long Sorties)>();

        foreach (var mouvement in Mouvements(requete.Elements, instantReference, finHorizonUtc))
        {
            // Rattachement au mois calendaire local — ce que l'utilisateur voit (D-004).
            var mois = MoisCalendaire.Depuis(mouvement.Locale);
            if (mois > dernierMois)
                continue;
            if (mois < moisReference)
                mois = moisReference; // cas limite fuseaux très à l'ouest (D-004)

            var (entrees, sorties) = flux.GetValueOrDefault(mois);
            if (mouvement.Sens == Sens.Entree)
                entrees += mouvement.Centimes;
            else
                sorties += mouvement.Centimes;
            flux[mois] = (entrees, sorties);
        }

        // La sortie hypothétique de la confrontation (§5.1bis) rejoint le flux comme n'importe
        // quelle autre sortie du mois cible : la cascade, elle, reste rigoureusement la même.
        if (sortieSupplementaire is { } extra)
        {
            var cible = extra.Mois < moisReference ? moisReference : extra.Mois;
            var (entrees, sorties) = flux.GetValueOrDefault(cible);
            flux[cible] = (entrees, sorties + extra.Centimes);
        }

        // Cascade mensuelle (§5.1.4) depuis le mois de la date de référence — le report de déficit
        // est automatique par construction (§5.1.5).
        var resultat = new List<MoisProjete>(requete.NombreMois);
        var solde = requete.Solde.Centimes;
        var mois2 = moisReference;
        var soldesAffiches = new Dictionary<MoisCalendaire, (long Ouverture, long Cloture)>();
        while (mois2 <= dernierMois)
        {
            var (entrees, sorties) = flux.GetValueOrDefault(mois2);
            var ouverture = solde;
            solde = solde + entrees - sorties;
            if (mois2 >= requete.PremierMois)
                soldesAffiches[mois2] = (ouverture, solde);
            mois2 = mois2.AjouterMois(1);
        }

        for (var i = 0; i < requete.NombreMois; i++)
        {
            var mois = requete.PremierMois.AjouterMois(i);
            if (mois < moisReference)
            {
                // Aucune projection possible avant le point de départ (§3.4, D-004).
                resultat.Add(new MoisProjete(mois.Annee, mois.Mois,
                    OuvertureCentimes: null, EntreesCentimes: 0, SortiesCentimes: 0,
                    ClotureCentimes: null, Decouvert: false, AvantReference: true));
            }
            else
            {
                var (entrees, sorties) = flux.GetValueOrDefault(mois);
                var (ouverture, cloture) = soldesAffiches[mois];
                resultat.Add(new MoisProjete(mois.Annee, mois.Mois,
                    ouverture, entrees, sorties, cloture,
                    Decouvert: cloture < 0, AvantReference: false));
            }
        }

        return resultat;
    }

    /// <summary>Une occurrence financière retenue : quand, combien, dans quel sens.</summary>
    private readonly record struct Mouvement(DateTimeOffset Utc, DateTime Locale, long Centimes, Sens Sens);

    /// <summary>
    /// Les occurrences financières à compter, de l'instant de référence à la fin de la fenêtre.
    /// <para>
    /// <b>Partagé entre la cascade mensuelle et le solde courant, et c'est le point.</b> Les deux
    /// répondent à la même question — « qu'est-ce qui bouge, et de combien » — et ne diffèrent que
    /// par la borne : un mois calendaire d'un côté, un instant de l'autre. Dupliquer les exclusions
    /// et l'expansion des RRULE les ferait diverger tôt ou tard, et il faudrait alors décider
    /// laquelle a raison.
    /// </para>
    /// </summary>
    private static IEnumerable<Mouvement> Mouvements(
        IReadOnlyList<Element> elements, DateTimeOffset instantReference, DateTimeOffset finFenetreUtc)
    {
        foreach (var element in elements)
        {
            // Exclusions (§5.1.3, D-004) : annulés, supprimés (corbeille), financiers sans date ou
            // sans montant. « EstFinancier » est AUSSI ce qui tient les envies hors du calcul
            // (§5.1bis, D-027) — le garde-fou n'est plus l'absence du champ montant, c'est ici.
            if (!element.EstFinancier || element.Supprime || element.Statut == StatutElement.Annule)
                continue;
            if (element.DateDebut is not { } dateDebut || element.MontantCentimes is not { } montant)
                continue;

            var fuseau = element.Fuseau is { Length: > 0 } id && FuseauxIana.Resoudre(id) is { } tz
                ? tz
                : TimeZoneInfo.Utc;
            var sens = element.Sens ?? (element.Type == TypeElement.Revenu ? Sens.Entree : Sens.Sortie);

            IEnumerable<Occurrence> occurrences = element.Recurrence is { Length: > 0 } rrule
                ? ExpanseurRecurrence.Expanser(RegleRecurrence.Analyser(rrule), dateDebut, fuseau, finFenetreUtc)
                : [ExpanseurRecurrence.OccurrenceUnique(dateDebut, fuseau)];

            foreach (var occurrence in occurrences)
            {
                if (occurrence.Utc < instantReference)
                    continue; // déjà contenu dans le solde de référence (§5.1.3)

                yield return new Mouvement(occurrence.Utc, occurrence.Locale, montant, sens);
            }
        }
    }

    /// <summary>
    /// Le <b>solde courant</b> à un instant donné (§5.1) — le chiffre que l'utilisateur lit en
    /// premier : <i>combien j'ai, maintenant</i>.
    /// <para>
    /// C'est la cascade du §5.1 <b>arrêtée à cet instant</b> plutôt qu'à une fin de mois : le solde
    /// de référence, plus le net des occurrences financières entre sa date et l'instant demandé.
    /// Mêmes exclusions, même expansion des RRULE, même arithmétique — <see cref="Mouvements"/> est
    /// partagé avec <see cref="Calculer"/> pour que ce soit vrai par construction et non par
    /// relecture.
    /// </para>
    /// <para>
    /// <b>Les envies n'y entrent jamais</b> (§5.1bis, D-027), pour la même raison qu'elles n'entrent
    /// pas dans la projection : elles ne sont pas financières. Une envie ne déplace donc pas ce
    /// chiffre, quel que soit le prix qu'on lui prête.
    /// </para>
    /// <para>
    /// <b>Jamais stocké</b> — calculé à la lecture, comme toute la projection (règle 9).
    /// </para>
    /// <para>
    /// Si l'instant précède la date de référence, l'intervalle est vide et le solde de référence est
    /// rendu tel quel : par définition, tout ce qui le précède y est déjà contenu (§5.1.3).
    /// </para>
    /// </summary>
    public static long SoldeCourant(SoldeReference solde, IReadOnlyList<Element> elements, DateTimeOffset instant)
    {
        var instantReference = new DateTimeOffset(solde.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        // Deux jours de marge à l'expansion : tout offset réel tient dans ±14 h, et le filtre
        // ci-dessous tranche ensuite à l'instant exact.
        var net = 0L;
        foreach (var mouvement in Mouvements(elements, instantReference, instant.AddDays(2)))
        {
            if (mouvement.Utc > instant)
                continue;
            net += mouvement.Sens == Sens.Entree ? mouvement.Centimes : -mouvement.Centimes;
        }

        return solde.Centimes + net;
    }

    /// <summary>
    /// Confrontation d'un montant au budget projeté (§5.1bis, NON NÉGOCIABLE).
    /// <para>
    /// <b>Lecture pure.</b> Rien n'est écrit, aucun Élément ni occurrence n'est créé : deux appels
    /// identiques rendent le même résultat et ne laissent rien derrière (règle 9).
    /// </para>
    /// <para>
    /// Rend les <b>deux cascades</b> et non un booléen — l'app doit pouvoir montrer l'écart mois
    /// par mois, pas seulement un oui/non.
    /// </para>
    /// </summary>
    public static Confrontation Confronter(RequeteConfrontation requete)
    {
        if (requete.MontantCentimes <= 0)
            throw new ArgumentOutOfRangeException(nameof(requete),
                "Le montant confronté doit être strictement positif (centimes entiers, règle 5).");

        var dernierMois = requete.Base.PremierMois.AjouterMois(requete.Base.NombreMois - 1);
        if (requete.MoisCible < requete.Base.PremierMois || requete.MoisCible > dernierMois)
            throw new ArgumentOutOfRangeException(nameof(requete),
                $"Mois cible « {requete.MoisCible} » hors de l'horizon "
                + $"({requete.Base.PremierMois} à {dernierMois}).");

        var nominale = Calculer(requete.Base, sortieSupplementaire: null);
        var simulee = Calculer(requete.Base, (requete.MoisCible, requete.MontantCentimes));

        // Verdict (§5.1bis.4) : on ne regarde QUE de la cible à la fin de l'horizon. Un mois déjà
        // négatif avant la cible n'est pas causé par cet achat, et le lui imputer ferait répondre
        // « non » à une dépense qui passe très bien.
        var casse = simulee.FirstOrDefault(m =>
            !m.AvantReference
            && new MoisCalendaire(m.Annee, m.Mois) >= requete.MoisCible
            && m.ClotureCentimes < 0);

        return new Confrontation(
            Nominale: nominale,
            Simulee: simulee,
            Passe: casse is null,
            PremierMoisQuiCasse: casse,
            // De combien ça manque, au pire moment — le chiffre qui dit quoi faire.
            ManqueCentimes: casse?.ClotureCentimes is { } c ? -c : 0);
    }
}
