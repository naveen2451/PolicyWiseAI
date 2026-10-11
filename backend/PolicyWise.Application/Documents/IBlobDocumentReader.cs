namespace PolicyWise.Application.Documents;

public interface IBlobDocumentReader
{
    Task<IReadOnlyList<BlobDocumentInfo>> ListDocumentsAsync(
        CancellationToken cancellationToken = default);

     Task<Stream> OpenReadAsync(
        string blobPath,
        CancellationToken cancellationToken = default);
}