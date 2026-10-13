using PolicyWise.Application.Search.Models;

namespace PolicyWise.Application.Evidence;

public interface IEvidenceValidator
{
    Task<EvidenceValidationResult> ValidateAsync(
        string question,
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        CancellationToken cancellationToken = default
    );
}
