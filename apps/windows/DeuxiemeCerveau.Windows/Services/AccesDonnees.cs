namespace DeuxiemeCerveau.Windows.Services;

/// <summary>
/// Sérialise TOUS les accès au graphe de services.
/// <para>
/// Nécessaire parce que <c>BaseLocale</c> détient une <c>SqliteConnection</c> UNIQUE partagée par
/// tout le graphe, et que <c>DepotLocal.DansTransaction</c> ouvre une transaction ADO non
/// réentrante : la synchro de fond ne doit jamais écrire pendant que l'interface lit.
/// </para>
/// </summary>
public sealed class AccesDonnees : IDisposable
{
    private readonly SemaphoreSlim _porte = new(1, 1);

    public T Lire<T>(Func<T> operation)
    {
        _porte.Wait();
        try { return operation(); }
        finally { _porte.Release(); }
    }

    public void Ecrire(Action operation)
    {
        _porte.Wait();
        try { operation(); }
        finally { _porte.Release(); }
    }

    /// <summary>Pour les opérations réseau (synchro, pièces jointes) qui touchent la base.</summary>
    public async Task ExecuterAsync(Func<CancellationToken, Task> operation, CancellationToken jeton = default)
    {
        await _porte.WaitAsync(jeton).ConfigureAwait(false);
        try { await operation(jeton).ConfigureAwait(false); }
        finally { _porte.Release(); }
    }

    public async Task<T> ExecuterAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken jeton = default)
    {
        await _porte.WaitAsync(jeton).ConfigureAwait(false);
        try { return await operation(jeton).ConfigureAwait(false); }
        finally { _porte.Release(); }
    }

    public void Dispose() => _porte.Dispose();
}
