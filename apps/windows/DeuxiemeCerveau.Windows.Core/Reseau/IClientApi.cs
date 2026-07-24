using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Windows.Core.Reseau;

public interface IClientApi
{
    Task<Guid> EnregistrerAppareilAsync(string nom, string plateforme, CancellationToken cancellationToken = default);
    Task<ReponsePush> PousserAsync(LotPush lot, CancellationToken cancellationToken = default);
    Task<PagePull> TirerAsync(long since, int limite = 5000, CancellationToken cancellationToken = default);
    Task RecalerSoldeAsync(DemandeRecalageSolde demande, CancellationToken cancellationToken = default);
    Task<ReponsePurge> PurgerAsync(LotPurge lot, CancellationToken cancellationToken = default);
}

public sealed class DemandeEnregistrementAppareilDto
{
    public string Nom { get; set; } = string.Empty;
    public string Plateforme { get; set; } = string.Empty;
}

public sealed class ReponseEnregistrementAppareilDto
{
    public Guid AppareilId { get; set; }
}

public sealed class DemandeRecalageSolde
{
    public Guid ChangeId { get; set; }
    public long SoldeReferenceCentimes { get; set; }
    public DateOnly SoldeReferenceDate { get; set; }
    public DateTimeOffset DateModification { get; set; }
    public Guid AppareilId { get; set; }

    public DemandeRecalageSolde(Guid changeId, long soldeReferenceCentimes, DateOnly soldeReferenceDate, DateTimeOffset dateModification, Guid appareilId)
    {
        ChangeId = changeId;
        SoldeReferenceCentimes = soldeReferenceCentimes;
        SoldeReferenceDate = soldeReferenceDate;
        DateModification = dateModification;
        AppareilId = appareilId;
    }
}
