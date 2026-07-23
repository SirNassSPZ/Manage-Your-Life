using System.Text.Json;
using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Api.Persistence;
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
public sealed class ServiceApi(
    IMagasinSynchro magasin, IMagasinAppareils appareils, IHorloge horloge, IStockagePieces stockage)
{
    private readonly object _verrou = new();

    /// <summary>Validité d'une URL SAS de pièce jointe (§7) : assez courte, assez longue pour un gros fichier.</summary>
    private static readonly TimeSpan DureeSas = TimeSpan.FromMinutes(15);

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

    // ----- Pièces jointes (§7, §8) : courtage d'URL SAS ; le binaire ne passe jamais par l'API -----

    /// <summary>
    /// <c>GET /attachments/upload-url</c> (§8) : prépare le téléversement d'un binaire vers Blob Storage.
    /// Renvoie l'identifiant de pièce, le chemin blob et une URL SAS d'écriture temporaire. Les
    /// métadonnées (PieceJointe) seront, elles, poussées par la synchro (§6.2) une fois l'envoi confirmé.
    /// </summary>
    public ReponseUrlEnvoiDto PreparerEnvoiPiece(Guid elementId, long tailleOctets, Guid? pieceId)
    {
        if (tailleOctets <= 0 || tailleOctets > PieceJointe.TailleMaxOctets)
            throw new PieceTropVolumineuse(tailleOctets);
        var id = pieceId ?? Guid.NewGuid();
        var blobPath = $"{elementId:D}/{id:D}"; // chemin opaque, dérivé des identifiants (jamais du nom de fichier)
        var (url, expire) = stockage.PreparerEnvoi(blobPath, DureeSas);
        return new ReponseUrlEnvoiDto(id, blobPath, url.ToString(), expire);
    }

    /// <summary>
    /// <c>POST /attachments/confirm</c> (§8) : vérifie que le binaire est bien arrivé dans le stockage
    /// (envoi terminé, §7). Renvoie la taille réelle. L'appelant peut alors marquer la pièce confirmée.
    /// </summary>
    public ReponseConfirmationDto ConfirmerEnvoiPiece(string blobPath)
    {
        var taille = stockage.TailleSiPresent(blobPath);
        if (taille is null)
            throw new TeleversementAbsent(blobPath);
        return new ReponseConfirmationDto(true, taille.Value);
    }

    /// <summary>
    /// <c>GET /attachments/{id}/download-url</c> (§8) : URL SAS de lecture. Le chemin blob est lu dans
    /// les métadonnées synchronisées (§6.2) ; une pièce inconnue ou supprimée (§7) est refusée en 404.
    /// </summary>
    public ReponseUrlLectureDto UrlLecturePiece(Guid pieceId)
    {
        var etat = magasin.Obtenir(EntiteSynchro.PieceJointe, pieceId);
        if (etat is null || etat.Supprime)
            throw new PieceIntrouvable(pieceId);
        var piece = SerialisationCanonique.Deserialiser<PieceJointe>(etat.PayloadCanonique);
        var (url, expire) = stockage.UrlLecture(piece.BlobPath, DureeSas);
        return new ReponseUrlLectureDto(url.ToString(), piece.NomFichier, expire);
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

/// <summary>Pièce jointe hors limite de taille (§7) — 400 côté HTTP.</summary>
public sealed class PieceTropVolumineuse(long taille)
    : Exception($"Pièce jointe de {taille} octets : hors limite (≤ {PieceJointe.TailleMaxOctets} octets, soit 25 Mo, §7).");

/// <summary>Confirmation demandée alors que le binaire n'est pas dans le stockage (§7) — 409 côté HTTP.</summary>
public sealed class TeleversementAbsent(string blobPath)
    : Exception($"Aucun téléversement présent pour « {blobPath} » : envoi non terminé (§7).");

/// <summary>URL de lecture demandée pour une pièce inconnue ou supprimée (§7) — 404 côté HTTP.</summary>
public sealed class PieceIntrouvable(Guid id)
    : Exception($"Pièce jointe {id} inconnue ou supprimée (§7).");
