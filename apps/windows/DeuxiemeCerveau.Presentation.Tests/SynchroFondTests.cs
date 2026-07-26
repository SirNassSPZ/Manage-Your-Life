namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Le déclenchement de la synchro (§6.2 « Cycle »). Ce qui manquait n'était pas le moteur — il est
/// complet et couvert par <c>MoteurSynchroTests</c> — mais <b>quelqu'un pour l'appeler</b>. Ces
/// tests couvrent ce quelqu'un.
/// <para>
/// Le cycle est reçu en paramètre (D-023), donc tout se vérifie sans serveur ni réseau.
/// </para>
/// </summary>
public class SynchroFondTests
{
    private static readonly TimeSpan Court = TimeSpan.FromMilliseconds(20);

    /// <summary>Attend qu'une condition devienne vraie, plutôt que de dormir une durée fixe.</summary>
    private static async Task Jusqua(Func<bool> condition, string quoi)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.Fail($"Toujours pas vrai après 2 s : {quoi}");
    }

    [Fact]
    public async Task Un_declenchement_lance_un_cycle()
    {
        var cycles = 0;
        using var service = new ServiceSynchroFond(
            _ => { Interlocked.Increment(ref cycles); return Task.CompletedTask; },
            () => true);

        await service.Declencher();

        Assert.Equal(1, Volatile.Read(ref cycles));
    }

    [Fact]
    public async Task Hors_ligne_aucun_cycle_ne_part()
    {
        // Hors ligne est le mode NOMINAL (filet 1), pas une panne : on ne tente rien, et sans bruit.
        var cycles = 0;
        using var service = new ServiceSynchroFond(
            _ => { Interlocked.Increment(ref cycles); return Task.CompletedTask; },
            () => false);

        await service.Declencher();

        Assert.Equal(0, Volatile.Read(ref cycles));
    }

    [Fact]
    public async Task La_possibilite_est_relue_a_chaque_fois()
    {
        // Se connecter en cours de session doit réveiller la synchro sans redémarrer l'app.
        var possible = false;
        var cycles = 0;
        using var service = new ServiceSynchroFond(
            _ => { Interlocked.Increment(ref cycles); return Task.CompletedTask; },
            () => possible);

        await service.Declencher();
        Assert.Equal(0, Volatile.Read(ref cycles));

        possible = true;
        await service.Declencher();

        Assert.Equal(1, Volatile.Read(ref cycles));
    }

    [Fact]
    public async Task Un_echec_reste_muet_et_ne_bloque_pas_le_suivant()
    {
        // Un serveur injoignable ne doit ni remonter d'exception ni condamner les cycles suivants :
        // l'outbox est persistante et le curseur est le point de reprise, rien n'est perdu.
        var appels = 0;
        using var service = new ServiceSynchroFond(
            _ => Interlocked.Increment(ref appels) == 1
                ? Task.FromException(new HttpRequestException("serveur injoignable"))
                : Task.CompletedTask,
            () => true);

        await service.Declencher();   // ne lève pas
        await service.Declencher();

        Assert.Equal(2, Volatile.Read(ref appels));
    }

    [Fact]
    public async Task Deux_cycles_ne_se_chevauchent_jamais()
    {
        // Deux cycles concurrents se disputeraient l'outbox. Le second RENONCE au lieu d'attendre :
        // le premier vide l'outbox en entier de toute façon.
        var demarres = 0;
        var relacher = new TaskCompletionSource();

        using var service = new ServiceSynchroFond(
            async _ => { Interlocked.Increment(ref demarres); await relacher.Task; },
            () => true);

        var premier = service.Declencher();
        await Jusqua(() => Volatile.Read(ref demarres) == 1, "le premier cycle a démarré");

        await service.Declencher();                       // renonce immédiatement
        Assert.Equal(1, Volatile.Read(ref demarres));

        relacher.SetResult();
        await premier;
    }

    [Fact]
    public async Task Une_rafale_de_saisies_ne_fait_qu_un_cycle()
    {
        // §6.2 « après toute saisie, différé de quelques secondes » : le différé existe pour
        // absorber une rafale, pas pour lancer un cycle par frappe.
        var cycles = 0;
        using var service = new ServiceSynchroFond(
            _ => { Interlocked.Increment(ref cycles); return Task.CompletedTask; },
            () => true,
            delaiApresSaisie: Court);

        for (var i = 0; i < 10; i++) service.DeclencherApresSaisie();

        await Jusqua(() => Volatile.Read(ref cycles) == 1, "le cycle différé est parti");
        await Task.Delay(80);

        Assert.Equal(1, Volatile.Read(ref cycles));
    }

    [Fact]
    public async Task Le_signal_de_fin_part_meme_quand_le_cycle_echoue()
    {
        // L'entête s'y accroche pour rafraîchir son compteur d'outbox. Ne le lever qu'en cas de
        // succès laisserait un compteur périmé à l'écran après chaque passage hors ligne.
        var fins = 0;
        using var service = new ServiceSynchroFond(
            _ => Task.FromException(new HttpRequestException("serveur injoignable")),
            () => true)
        {
            ApresCycle = () => Interlocked.Increment(ref fins),
        };

        await service.Declencher();

        Assert.Equal(1, Volatile.Read(ref fins));
    }
}
