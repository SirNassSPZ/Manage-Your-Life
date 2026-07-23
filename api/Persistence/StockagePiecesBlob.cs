using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace DeuxiemeCerveau.Api.Persistence;

/// <summary>
/// Implémentation d'<see cref="IStockagePieces"/> sur Azure Blob Storage (adaptateur, règle 4).
/// Signe les URL SAS avec la clé de compte fournie par la chaîne de connexion du runtime
/// (<c>AzureWebJobsStorage</c>, injectée par Bicep — jamais dans git, règle 16). Ce chemin évite
/// l'attribution de rôle RBAC (que le principal de déploiement CI n'a pas le droit de poser).
/// </summary>
public sealed class StockagePiecesBlob : IStockagePieces
{
    private readonly BlobContainerClient _conteneur;

    public StockagePiecesBlob(string chaineConnexion, string nomConteneur)
        => _conteneur = new BlobContainerClient(chaineConnexion, nomConteneur);

    public (Uri Url, DateTimeOffset ExpireLe) PreparerEnvoi(string blobPath, TimeSpan duree)
        => Signer(blobPath, BlobSasPermissions.Write | BlobSasPermissions.Create, duree);

    public (Uri Url, DateTimeOffset ExpireLe) UrlLecture(string blobPath, TimeSpan duree)
        => Signer(blobPath, BlobSasPermissions.Read, duree);

    public long? TailleSiPresent(string blobPath)
    {
        var blob = _conteneur.GetBlobClient(blobPath);
        return blob.Exists().Value ? blob.GetProperties().Value.ContentLength : null;
    }

    private (Uri Url, DateTimeOffset ExpireLe) Signer(string blobPath, BlobSasPermissions droits, TimeSpan duree)
    {
        var blob = _conteneur.GetBlobClient(blobPath);
        if (!blob.CanGenerateSasUri)
            throw new InvalidOperationException(
                "Le client de stockage ne peut pas signer d'URL SAS : chaîne de connexion sans clé de compte.");

        var expire = DateTimeOffset.UtcNow.Add(duree);
        var constructeur = new BlobSasBuilder(droits, expire)
        {
            BlobContainerName = _conteneur.Name,
            BlobName = blobPath,
            Resource = "b", // « b » : la SAS porte sur un blob précis
        };
        return (blob.GenerateSasUri(constructeur), expire);
    }
}
