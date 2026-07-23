using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>Un appareil enregistré (§6.2, table devices §9). Non synchronisé — propre au serveur.</summary>
public sealed record Appareil(Guid Id, string Nom, string Plateforme, DateTimeOffset DateEnregistrement);

/// <summary>
/// Registre des appareils (§6.2). Abstraction de stockage — l'adaptateur (SQL) fournit l'implémentation ;
/// le cœur définit le contrat. Aucune dépendance d'hébergeur (règle 4).
/// </summary>
public interface IMagasinAppareils
{
    /// <summary>Enregistre un appareil et renvoie son identité serveur.</summary>
    Appareil Enregistrer(string nom, string plateforme);

    Appareil? Obtenir(Guid id);
}

/// <summary>Implémentation mémoire de référence (tests + incrément 3a).</summary>
public sealed class MagasinAppareilsMemoire : IMagasinAppareils
{
    private readonly Dictionary<Guid, Appareil> _appareils = new();
    private readonly IHorloge _horloge;

    public MagasinAppareilsMemoire(IHorloge horloge) => _horloge = horloge;

    public Appareil Enregistrer(string nom, string plateforme)
    {
        var appareil = new Appareil(Guid.NewGuid(), nom, plateforme, _horloge.MaintenantUtc);
        _appareils[appareil.Id] = appareil;
        return appareil;
    }

    public Appareil? Obtenir(Guid id) => _appareils.GetValueOrDefault(id);
}
