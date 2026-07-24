using DeuxiemeCerveau.App.Local;
using DeuxiemeCerveau.App.Synchro;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.App.Services;

/// <summary>
/// Purge définitive depuis la corbeille (§5.6) — la seule destruction réelle de l'application, sur
/// confirmation explicite de l'utilisateur. Local-first : détruit la copie locale IMMÉDIATEMENT et
/// abandonne les changements en attente de l'entité, puis met la demande en file pour le serveur
/// (envoyée au prochain cycle de synchro). Le serveur arbitre (D-010) : si l'entité a été restaurée
/// entre-temps, il refuse — la conservation gagne, et le prochain pull restitue l'entité.
/// </summary>
public sealed class ServicePurge(DepotLocal depot, FilePurges file)
{
    public void Purger(EntiteSynchro type, Guid id)
    {
        depot.DansTransaction(() =>
        {
            depot.SupprimerReel(type, id);      // destruction locale immédiate (§5.6, local-first)
            depot.ViderOutboxEntite(type, id);  // abandonner les changements en attente de l'entité
        });
        file.Ajouter(new PurgeEnAttente(Guid.NewGuid(), type, id));
    }
}
