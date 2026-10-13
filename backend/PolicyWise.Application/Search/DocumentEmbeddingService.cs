using PolicyWise.Application.Chunking.Models;
using PolicyWise.Application.Embeddings;
using PolicyWise.Application.Search.Models;

namespace PolicyWise.Application.Search;

public sealed class DocumentEmbeddingService
{
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly ISearchDocumentIndexer _indexer;

    public DocumentEmbeddingService(
        ITextEmbeddingGenerator embeddingGenerator,
        ISearchDocumentIndexer indexer
    )
    {
        _embeddingGenerator = embeddingGenerator;
        _indexer = indexer;
    }

    public async Task<int> ProcessAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default
    )
    {
        if (chunks.Count == 0)
            return 0;

        var embeddedChunks = new List<EmbeddedDocumentChunk>();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var embedding = await _embeddingGenerator.GenerateAsync(
                chunk.Content,
                cancellationToken
            );

            embeddedChunks.Add(new EmbeddedDocumentChunk(chunk, embedding));
        }

        await _indexer.IndexChunksAsync(embeddedChunks, cancellationToken);

        return embeddedChunks.Count;
    }
}
