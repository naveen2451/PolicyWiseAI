using PolicyWise.Application.Search.Models;

namespace PolicyWise.Application.Search;

public interface IDocumentRetriever
{
    Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        string? policyType = null,
        string? question = null,
        CancellationToken cancellationToken = default
    );
}
