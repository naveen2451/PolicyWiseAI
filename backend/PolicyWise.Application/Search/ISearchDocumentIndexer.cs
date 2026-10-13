using PolicyWise.Application.Search.Models;

namespace PolicyWise.Application.Search;

public interface ISearchDocumentIndexer
{
    Task IndexChunksAsync(
        IReadOnlyList<EmbeddedDocumentChunk> chunks,
        CancellationToken cancellationToken = default
    );
}
