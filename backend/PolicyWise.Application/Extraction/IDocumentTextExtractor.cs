using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Extraction;

public interface IDocumentTextExtractor
{
    Task<ExtractedDocument> ExtractAsync(
        string documentId,
        string blobPath,
        string policyType,
        Stream documentStream,
        CancellationToken cancellationToken = default);
}