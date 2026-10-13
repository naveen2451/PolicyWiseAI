using System.Diagnostics;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using PolicyWise.Application.Search;
using PolicyWise.Application.Search.Models;

namespace PolicyWise.Infrastructure.Search;

public sealed class AzureSearchDocumentRetriever : IDocumentRetriever
{
    private readonly SearchClient _searchClient;
    private readonly ILogger<AzureSearchDocumentRetriever> _logger;

    public AzureSearchDocumentRetriever(
        string endpoint,
        string indexName,
        ILogger<AzureSearchDocumentRetriever> logger
    )
    {
        _searchClient = new SearchClient(new Uri(endpoint), indexName, new AzureCliCredential());

        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        string? policyType = null,
        string? question = null,
        CancellationToken cancellationToken = default
    )
    {
        if (queryEmbedding.Length != 1536)
            throw new ArgumentException("Query embedding must contain 1536 dimensions.");

        if (topK is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(topK));

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Vector search started. TopK={TopK}, PolicyType={PolicyType}, Dimensions={Dimensions}",
            topK,
            policyType ?? "ALL",
            queryEmbedding.Length
        );

        var options = new SearchOptions
        {
            Size = topK,
            Select = { "id", "documentId", "policyType", "sectionTitle", "content", "pageNumbers" },
        };

        options.VectorSearch = new VectorSearchOptions();

        options.VectorSearch.Queries.Add(
            new VectorizedQuery(queryEmbedding.ToArray())
            {
                KNearestNeighborsCount = topK,
                Fields = { "contentVector" },
            }
        );

        if (!string.IsNullOrWhiteSpace(policyType))
        {
            options.Filter = SearchFilter.Create($"policyType eq {policyType}");
        }

        var response = await _searchClient.SearchAsync<SearchDocument>(
            searchText: question,
            options: options,
            cancellationToken: cancellationToken
        );

        var results = new List<RetrievedDocumentChunk>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var document = result.Document;

            var pageNumbers =
                document.TryGetValue("pageNumbers", out var pages) && pages is not null
                    ? System.Text.Json.JsonSerializer.Deserialize<int[]>(
                        System.Text.Json.JsonSerializer.Serialize(pages)
                    ) ?? []
                    : [];

            results.Add(
                new RetrievedDocumentChunk(
                    ChunkId: document["id"]?.ToString() ?? "",
                    DocumentId: document["documentId"]?.ToString() ?? "",
                    PolicyType: document["policyType"]?.ToString() ?? "",
                    SectionTitle: document["sectionTitle"]?.ToString() ?? "",
                    Content: document["content"]?.ToString() ?? "",
                    PageNumbers: pageNumbers,
                    Score: result.Score ?? 0
                )
            );
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Vector search completed. Results={Count}, DurationMs={DurationMs}",
            results.Count,
            stopwatch.ElapsedMilliseconds
        );

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];

            _logger.LogInformation(
                "Rank={Rank}, Score={Score:F4}, Document={DocumentId}, Section={Section}, Pages={Pages}",
                i + 1,
                result.Score,
                result.DocumentId,
                result.SectionTitle,
                string.Join(",", result.PageNumbers)
            );
        }

        return results;
    }
}
