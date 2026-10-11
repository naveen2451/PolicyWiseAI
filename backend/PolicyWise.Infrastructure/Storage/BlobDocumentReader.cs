using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using PolicyWise.Application.Documents;

namespace PolicyWise.Infrastructure.Storage;

public sealed class BlobDocumentReader : IBlobDocumentReader
{
    private readonly BlobContainerClient _containerClient;

    public BlobDocumentReader(
        BlobServiceClient blobServiceClient,
        IConfiguration configuration)
    {
        var containerName =
            configuration["Azure:Storage:ContainerName"]
            ?? throw new InvalidOperationException(
                "Blob container name is missing.");

        _containerClient =
            blobServiceClient.GetBlobContainerClient(containerName);
    }

    public async Task<IReadOnlyList<BlobDocumentInfo>>
        ListDocumentsAsync(
            CancellationToken cancellationToken = default)
    {
        var documents = new List<BlobDocumentInfo>();

        foreach (var policyType in new[] { "car", "life" })
        {
            await foreach (var blob in _containerClient.GetBlobsAsync(
    traits: BlobTraits.None,
    states: BlobStates.None,
    prefix: $"{policyType}/",
    cancellationToken: cancellationToken))
            {
                if (!blob.Name.EndsWith(
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                documents.Add(new BlobDocumentInfo(
                    BlobPath: blob.Name,
                    FileName: Path.GetFileName(blob.Name),
                    PolicyType: policyType,
                    SizeBytes: blob.Properties.ContentLength ?? 0,
                    LastModified: blob.Properties.LastModified
                ));
            }
        }

        return documents
            .OrderBy(d => d.PolicyType)
            .ThenBy(d => d.FileName)
            .ToList();
    }

    public async Task<Stream> OpenReadAsync(
    string blobPath,
    CancellationToken cancellationToken = default)
{
    if (!(blobPath.StartsWith("car/", StringComparison.Ordinal) ||
          blobPath.StartsWith("life/", StringComparison.Ordinal)) ||
        !blobPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("Unsupported document path.");
    }

    var blobClient = _containerClient.GetBlobClient(blobPath);

    return await blobClient.OpenReadAsync(
        cancellationToken: cancellationToken);
}
}