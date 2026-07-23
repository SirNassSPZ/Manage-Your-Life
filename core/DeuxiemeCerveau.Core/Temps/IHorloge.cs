namespace DeuxiemeCerveau.Core.Temps;

/// <summary>Horloge injectable — le moteur de synchro horodate « recu_le » sans dépendre de l'horloge système.</summary>
public interface IHorloge
{
    DateTimeOffset MaintenantUtc { get; }
}

public sealed class HorlogeSysteme : IHorloge
{
    public DateTimeOffset MaintenantUtc => DateTimeOffset.UtcNow;
}

/// <summary>Horloge fixe pour les tests.</summary>
public sealed class HorlogeFixe(DateTimeOffset instant) : IHorloge
{
    public DateTimeOffset MaintenantUtc { get; set; } = instant;
}
