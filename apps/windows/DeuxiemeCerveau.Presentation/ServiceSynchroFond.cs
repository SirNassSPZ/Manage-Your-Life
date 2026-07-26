namespace DeuxiemeCerveau.Presentation;

/// <summary>
/// Déclenche la synchronisation (§6.2 « Cycle »). <b>Le moteur existait et personne ne l'appelait</b> :
/// <c>MoteurSynchro.Synchroniser</c> n'était atteint que par les tests et le mode <c>--parite</c>.
/// L'outbox ne pouvait donc que grossir, et la projection — calculée par le serveur (règle 9) — ne
/// voyait jamais ce que l'utilisateur saisissait.
/// <para>
/// Le §6.2 impose quatre déclencheurs : <b>à l'ouverture</b>, <b>après toute saisie</b> (différé de
/// quelques secondes), <b>au retour du réseau</b>, et <b>périodiquement</b>. Les quatre passent par
/// <see cref="Declencher"/> : un seul chemin, donc un seul comportement à retranscrire en Swift.
/// </para>
/// <para>
/// <b>Le cycle est reçu en paramètre</b>, comme la remise de <c>PlanificateurRappels.Derouler</c>
/// (D-023). C'est ce qui rend le déclenchement testable sans serveur : l'app branche le vrai
/// moteur, un test branche un compteur.
/// </para>
/// <para>
/// <b>L'interface n'attend jamais</b> (filet 1) : <see cref="Declencher"/> rend la main tout de
/// suite. Un échec est <b>muet</b> — hors ligne est le mode nominal, pas une panne à signaler. Ce
/// qui se voit, c'est le compteur d'outbox de l'entête, qui redescend quand le push passe.
/// </para>
/// </summary>
public sealed class ServiceSynchroFond : IDisposable
{
    /// <summary>
    /// Délai après une saisie. Le §6.2 dit « différé de quelques secondes » : de quoi absorber une
    /// rafale de saisies en un seul cycle plutôt qu'un cycle par frappe.
    /// </summary>
    public static readonly TimeSpan DelaiApresSaisie = TimeSpan.FromSeconds(5);

    /// <summary>Battement de fond. Assez rare pour rester invisible, assez fréquent pour rattraper.</summary>
    public static readonly TimeSpan Periode = TimeSpan.FromMinutes(15);

    private readonly Func<CancellationToken, Task> _cycle;
    private readonly Func<bool> _possible;
    private readonly TimeSpan _delaiApresSaisie;
    private readonly CancellationTokenSource _arret = new();

    /// <summary>Garantit un seul cycle à la fois : deux cycles concurrents se disputeraient l'outbox.</summary>
    private readonly SemaphoreSlim _unSeulCycle = new(1, 1);

    private CancellationTokenSource? _differe;

    /// <param name="cycle">Un cycle complet §6.2 — en production, <c>MoteurSynchro.Synchroniser</c>.</param>
    /// <param name="possible">
    /// Vrai si la synchro a un sens maintenant (API configurée et utilisateur connecté). Relu à
    /// <b>chaque</b> déclenchement : se connecter en cours de session doit suffire à réveiller la
    /// synchro, sans redémarrer l'application.
    /// </param>
    /// <param name="delaiApresSaisie">Réglable pour les tests, qui n'attendent pas cinq secondes.</param>
    public ServiceSynchroFond(
        Func<CancellationToken, Task> cycle,
        Func<bool> possible,
        TimeSpan? delaiApresSaisie = null)
    {
        _cycle = cycle;
        _possible = possible;
        _delaiApresSaisie = delaiApresSaisie ?? DelaiApresSaisie;
    }

    /// <summary>Levé après chaque cycle, réussi ou non — l'entête rafraîchit son compteur d'outbox.</summary>
    public Action? ApresCycle { get; set; }

    /// <summary>Vrai pendant un cycle. Sert à l'affichage, jamais à bloquer quoi que ce soit.</summary>
    public bool EnCours { get; private set; }

    /// <summary>
    /// Lance un cycle en tâche de fond et rend la main <b>immédiatement</b>. Rend la tâche pour que
    /// les tests puissent l'attendre ; l'application, elle, l'ignore délibérément.
    /// <para>
    /// Si un cycle tourne déjà, celui-ci <b>renonce</b> au lieu d'attendre son tour : empiler les
    /// cycles rejouerait le même travail, puisque chaque passage vide l'outbox en entier.
    /// </para>
    /// </summary>
    public Task Declencher()
    {
        if (!_possible() || _arret.IsCancellationRequested)
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            if (!await _unSeulCycle.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
                return;

            EnCours = true;
            try
            {
                await _cycle(_arret.Token).ConfigureAwait(false);
            }
            catch
            {
                // Muet par choix : hors ligne est le mode nominal (filet 1). Le prochain
                // déclencheur reprendra — l'outbox est persistante et le curseur est le point de
                // reprise, donc rien n'est perdu à échouer ici.
            }
            finally
            {
                EnCours = false;
                _unSeulCycle.Release();
                ApresCycle?.Invoke();
            }
        });
    }

    /// <summary>
    /// Après une saisie (§6.2). Différé et <b>redéclenchable</b> : dix saisies d'affilée ne font
    /// qu'un cycle, quelques secondes après la dernière.
    /// </summary>
    public void DeclencherApresSaisie()
    {
        if (!_possible()) return;

        var precedent = Interlocked.Exchange(ref _differe, new CancellationTokenSource());
        precedent?.Cancel();
        precedent?.Dispose();

        var jeton = _differe!.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delaiApresSaisie, jeton).ConfigureAwait(false);
                await Declencher().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Une saisie plus récente a repoussé l'échéance : c'est le comportement voulu.
            }
        });
    }

    /// <summary>
    /// Démarre les déclencheurs permanents : un cycle <b>à l'ouverture</b>, un par période, et le
    /// <b>retour du réseau</b>. Sans le battement, une application laissée ouverte cesserait de
    /// synchroniser — le même défaut que la minuterie des rappels corrigeait (D-023).
    /// </summary>
    public void Demarrer()
    {
        _ = Declencher();

        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += SurReseau;

        _ = Task.Run(async () =>
        {
            while (!_arret.IsCancellationRequested)
            {
                try { await Task.Delay(Periode, _arret.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                await Declencher().ConfigureAwait(false);
            }
        });
    }

    private void SurReseau(object? _, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable) _ = Declencher();
    }

    public void Dispose()
    {
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= SurReseau;
        _arret.Cancel();
        _differe?.Cancel();
        _differe?.Dispose();
        _arret.Dispose();
        _unSeulCycle.Dispose();
    }
}
