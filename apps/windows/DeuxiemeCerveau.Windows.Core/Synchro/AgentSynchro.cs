using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Windows.Core.Depot;
using DeuxiemeCerveau.Windows.Core.Reseau;

namespace DeuxiemeCerveau.Windows.Core.Synchro;

/// <summary>
/// Agent de synchronisation client (Filet 1, 2, 3 & Curseur §6).
/// Exécute les cycles Push -> Pull en arrière-plan et réessaie automatiquement en cas de coupure.
/// </summary>
public sealed class AgentSynchro
{
    private readonly IDepotLocal _depot;
    private readonly IClientApi _clientApi;
    private readonly string _nomAppareil;
    private readonly string _plateforme;

    public AgentSynchro(IDepotLocal depot, IClientApi clientApi, string nomAppareil = "App Windows", string plateforme = "windows")
    {
        _depot = depot;
        _clientApi = clientApi;
        _nomAppareil = nomAppareil;
        _plateforme = plateforme;
    }

    /// <summary>
    /// S'assure que l'appareil est enregistré auprès du serveur (POST /devices/register).
    /// </summary>
    public async Task SynchroniserAppareilAsync(CancellationToken cancellationToken = default)
    {
        if (_depot.AppareilId == Guid.Empty)
        {
            var nouvelId = await _clientApi.EnregistrerAppareilAsync(_nomAppareil, _plateforme, cancellationToken);
            _depot.DefinirAppareilId(nouvelId);
        }
    }

    /// <summary>
    /// Exécute un cycle complet : Enregistrement -> Push outbox -> Pull par curseur.
    /// </summary>
    public async Task<bool> SynchroniserAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SynchroniserAppareilAsync(cancellationToken);

            // 1. PUSH : dépiler l'outbox locale
            var outbox = _depot.ObtenirOutbox();
            if (outbox.Count > 0)
            {
                var lot = new LotPush
                {
                    AppareilId = _depot.AppareilId,
                    Changements = outbox.Select(o => o.VersChangementPush()).ToList()
                };

                var reponsePush = await _clientApi.PousserAsync(lot, cancellationToken);
                var confirmes = reponsePush.Resultats
                    .Where(r => r.Resultat is ResultatChangement.Applique or ResultatChangement.PerdantArchive)
                    .Select(r => r.ChangeId)
                    .ToList();

                _depot.NettoyerOutbox(confirmes);
            }

            // 2. PULL : récupérer les changements depuis le curseur
            var curseur = _depot.ObtenirCurseurPull();
            var pagePull = await _clientApi.TirerAsync(curseur, 5000, cancellationToken);
            _depot.AppliquerPull(pagePull);

            return true;
        }
        catch
        {
            // Erreur réseau ou coupure : l'outbox reste intacte et sera réessayée au prochain cycle (Local-First §6).
            return false;
        }
    }
}
