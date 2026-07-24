using System.Text.Json;
using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;
using DeuxiemeCerveau.Core.Validation;

namespace DeuxiemeCerveau.App.Services;

/// <summary>Résultat d'une saisie : réussie, ou rejetée avec les erreurs de validation (§3.1, §3.6).</summary>
public sealed record ResultatSaisie(bool Reussi, IReadOnlyList<ErreurValidation> Erreurs)
{
    public static readonly ResultatSaisie Ok = new(true, []);
    public static ResultatSaisie Rejete(IReadOnlyList<ErreurValidation> erreurs) => new(false, erreurs);
}

/// <summary>
/// Saisie « locale d'abord » (§6.1, filet 1) : valide via le cœur (mêmes validateurs que le serveur),
/// écrit dans la base locale ET pose une entrée d'<c>outbox</c> persistante — puis rend la main
/// IMMÉDIATEMENT, sans aucun réseau. La confirmation à l'utilisateur = l'écriture locale ; l'envoi
/// effectif au serveur vient plus tard (client de synchro, incrément 4d). Une suppression est un
/// marquage (filet 2), jamais un effacement réel. server_seq n'est jamais posé côté client (§6.2).
/// </summary>
public sealed class ServiceSaisie(DepotLocal depot, IdentiteAppareil identite, IHorloge horloge)
{
    /// <summary>Crée (id vide) ou modifie une entité. L'audit (version, dates, appareil) est géré ici.</summary>
    public ResultatSaisie Enregistrer(EntiteSynchronisee entite, EntiteSynchro type)
    {
        if (entite.Id == Guid.Empty)
            entite.Id = Guid.NewGuid();

        var existant = depot.Obtenir(type, entite.Id);
        var maintenant = horloge.MaintenantUtc;
        if (existant is null)
        {
            if (entite.DateCreation == default)
                entite.DateCreation = maintenant;
            entite.Version = 1;
        }
        else
        {
            entite.Version = existant.Version + 1; // le compteur ne régresse jamais (§6.2)
        }
        entite.DateModification = maintenant;
        entite.AppareilSource = identite.Obtenir();
        entite.ServerSeq = null; // posé par le serveur au pull (§6.2), jamais par le client

        var erreurs = AiguilleurEntites.Valider(type, entite);
        if (erreurs.Count > 0)
            return ResultatSaisie.Rejete(erreurs);

        Poser(entite, type);
        return ResultatSaisie.Ok;
    }

    /// <summary>Met à la corbeille (filet 2 : marquage <c>supprime = true</c>, jamais un DELETE).</summary>
    public ResultatSaisie Supprimer(EntiteSynchro type, Guid id) => Basculer(type, id, supprime: true);

    /// <summary>Restaure depuis la corbeille en un geste (§5.6).</summary>
    public ResultatSaisie Restaurer(EntiteSynchro type, Guid id) => Basculer(type, id, supprime: false);

    private ResultatSaisie Basculer(EntiteSynchro type, Guid id, bool supprime)
    {
        var existant = depot.Obtenir(type, id);
        if (existant is null)
            return ResultatSaisie.Rejete([new ErreurValidation("id", "introuvable", "Entité inconnue en local.")]);
        var entite = AiguilleurEntites.Deserialiser(type, existant.PayloadCanonique);
        entite.Supprime = supprime;
        entite.DateSuppression = supprime ? horloge.MaintenantUtc : null;
        return Enregistrer(entite, type); // repasse par l'audit (version++), la validation, local + outbox
    }

    /// <summary>Filet 1 : écriture locale et entrée d'outbox dans une seule transaction locale (sans réseau).</summary>
    private void Poser(EntiteSynchronisee entite, EntiteSynchro type)
    {
        var payload = AiguilleurEntites.Serialiser(type, entite);
        var etat = new EtatEntite(type, entite.Id, entite.Version, entite.DateModification,
            entite.Supprime, entite.ServerSeq ?? 0, payload);
        var changement = new ChangementPush
        {
            ChangeId = Guid.NewGuid(), // UUID local — une entrée d'outbox par modification (§6.2)
            Entite = type,
            EntiteId = entite.Id,
            Version = entite.Version,
            DateModification = entite.DateModification,
            AppareilId = entite.AppareilSource,
            Payload = JsonDocument.Parse(payload).RootElement.Clone(),
        };
        depot.DansTransaction(() =>
        {
            depot.Ecrire(etat);
            depot.AjouterOutbox(changement);
        });
    }
}
