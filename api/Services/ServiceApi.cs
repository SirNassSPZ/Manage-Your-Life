using System.Text.Json;
using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Projection;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.Api.Services;

/// <summary>
/// Orchestration de l'API (§8) — adaptateur mince autour du cœur (règle 4). Sérialise les opérations
/// mutantes (le moteur n'est pas thread-safe, D-005) ; en SQL, la transaction assure l'atomicité.
/// </summary>
public sealed class ServiceApi(IMagasinSynchro magasin, IMagasinAppareils appareils, IHorloge horloge)
{
    private readonly object _verrou = new();

    public ReponseEnregistrementAppareil EnregistrerAppareil(DemandeEnregistrementAppareil demande)
    {
        var appareil = appareils.Enregistrer(demande.Nom, demande.Plateforme);
        return new ReponseEnregistrementAppareil(appareil.Id);
    }

    public ReponsePushDto Pousser(LotPush lot)
    {
        ReponsePush reponse;
        lock (_verrou)
            reponse = new ProcesseurPush(magasin, horloge).Traiter(lot);
        return new ReponsePushDto(
            reponse.Resultats.Select(r => new ResultatChangementDto(
                r.ChangeId, r.Resultat, r.Conflit, r.ServerSeq, r.Rejoue)).ToList(),
            reponse.ChangementsInduits.Select(c => new ChangementInduitDto(
                c.ChangeId, c.Entite, c.EntiteId, c.ServerSeq)).ToList());
    }

    public ReponsePullDto Tirer(long depuis, int limite)
    {
        var page = new ProcesseurPull(magasin).Traiter(depuis, limite);
        return new ReponsePullDto(
            page.Entites.Select(e => new EntitePullDto(
                e.Entite, e.Id, e.Version, e.DateModification, e.Supprime, e.ServerSeq,
                JsonSerializer.Deserialize<JsonElement>(e.PayloadCanonique))).ToList(),
            page.Purges.Select(p => new PurgePullDto(p.Entite, p.Id, p.ServerSeq, p.PurgeLe)).ToList(),
            page.Curseur, page.Encore);
    }

    public ReponsePushDto Recaler(DemandeRecalageSolde demande)
    {
        var recalage = new RecalageSolde
        {
            ChangeId = demande.ChangeId,
            SoldeReferenceCentimes = demande.SoldeReferenceCentimes,
            SoldeReferenceDate = demande.SoldeReferenceDate,
            DateModification = demande.DateModification,
            AppareilId = demande.AppareilId,
        };
        ResultatPush resultat;
        lock (_verrou)
            resultat = new ProcesseurReglage(magasin, horloge).Recaler(recalage);
        return new ReponsePushDto(
            [new ResultatChangementDto(resultat.ChangeId, resultat.Resultat, resultat.Conflit,
                resultat.ServerSeq, resultat.Rejoue)],
            []);
    }

    public ReponsePurge Purger(LotPurge lot)
    {
        lock (_verrou)
            return new ProcesseurPurge(magasin, horloge).Traiter(lot);
    }

    /// <summary>
    /// Projection budgétaire (§5.1) : lit le solde de référence et tous les Éléments du magasin,
    /// calcule à la lecture (jamais stocké, règle 9). Le premier mois est le mois courant.
    /// </summary>
    public ReponseProjectionDto Projeter(int nombreMois)
    {
        var reglage = new ProcesseurReglage(magasin, horloge).Lire();
        if (reglage is null)
            throw new SoldeReferenceAbsent();

        var elements = magasin.EnumererEtats(EntiteSynchro.Element)
            .Select(e => SerialisationCanonique.Deserialiser<Element>(e.PayloadCanonique))
            .ToList();

        var maintenant = ConvertisseurFuseau.VersLocale(horloge.MaintenantUtc, TimeZoneInfo.Utc);
        var requete = new RequeteProjection(
            new MoisCalendaire(maintenant.Year, maintenant.Month),
            nombreMois,
            new SoldeReference(reglage.SoldeReferenceCentimes, reglage.SoldeReferenceDate),
            elements);

        var resultat = CalculateurProjection.Calculer(requete);
        return new ReponseProjectionDto(resultat.Select(m => new MoisProjeteDto(
            $"{m.Annee:D4}-{m.Mois:D2}", m.OuvertureCentimes, m.EntreesCentimes, m.SortiesCentimes,
            m.ClotureCentimes, m.Decouvert, m.AvantReference)).ToList());
    }
}

/// <summary>Levée quand la projection est demandée sans solde de référence (§3.4) — 409 côté HTTP.</summary>
public sealed class SoldeReferenceAbsent()
    : Exception("Aucun solde de référence n'a été saisi (§3.4) : la projection est impossible.");
