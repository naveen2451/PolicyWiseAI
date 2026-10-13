using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using PolicyWise.Application.Search;
using PolicyWise.Application.Search.Models;

namespace PolicyWise.Infrastructure.Search;

public sealed class AzureSearchDocumentIndexer : ISearchDocumentIndexer
{
    private readonly SearchClient _searchClient;

    public AzureSearchDocumentIndexer(string endpoint, string indexName)
    {
        _searchClient = new SearchClient(new Uri(endpoint), indexName, new AzureCliCredential());
    }

    public async Task IndexChunksAsync(
        IReadOnlyList<EmbeddedDocumentChunk> chunks,
        CancellationToken cancellationToken = default
    )
    {
        if (chunks.Count == 0)
            return;

        var documents = chunks
            .Select(item =>
            {
                var chunk = item.Chunk;

                if (item.Embedding.Length != 1536)
                    throw new InvalidOperationException(
                        $"Invalid embedding dimensions for {chunk.ChunkId}"
                    );

                return new PolicyWiseSearchRecord
                {
                    Id = chunk.ChunkId,
                    DocumentId = chunk.DocumentId,
                    DocumentVersion = chunk.SourceVersion,
                    FileName = Path.GetFileName(chunk.BlobPath),
                    BlobPath = chunk.BlobPath,
                    PolicyType = chunk.PolicyType,
                    SectionTitle = chunk.SectionTitle,
                    Content = chunk.Content,
                    PageNumbers = chunk.PageNumbers.ToArray(),
                    ChunkIndex = chunk.ChunkIndex,
                    ChunkingStrategyVersion = chunk.ChunkingStrategyVersion,
                    ContentVector = item.Embedding.ToArray(),
                };
            })
            .ToList();
        var firstDocument = documents.First();

        // var indexClient = new SearchIndexClient(_searchClient.Endpoint, new AzureCliCredential());

        // var indexResponse = await indexClient.GetIndexAsync(
        //     _searchClient.IndexName,
        //     cancellationToken
        // );

        // foreach (var field in indexResponse.Value.Fields)
        // {
        //     Console.WriteLine($"Field: {field.Name}, Type: {field.Type}");
        // }
        var batch = IndexDocumentsBatch.Upload(documents);

        var response = await _searchClient.IndexDocumentsAsync(
            batch,
            cancellationToken: cancellationToken
        );

        var failures = response.Value.Results.Where(result => !result.Succeeded).ToList();

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Failed to index {failures.Count} chunks: "
                    + string.Join(", ", failures.Select(f => f.Key))
            );
        }
    }
}
