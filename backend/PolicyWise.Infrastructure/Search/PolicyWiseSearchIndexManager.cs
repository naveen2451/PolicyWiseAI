using Azure;
using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace PolicyWise.Infrastructure.Search;

public sealed class PolicyWiseSearchIndexManager
{
    private readonly SearchIndexClient _indexClient;

    public PolicyWiseSearchIndexManager(string endpoint)
    {
        _indexClient = new SearchIndexClient(new Uri(endpoint), new AzureCliCredential());
    }

    public async Task CreateIndexAsync(
        string indexName,
        CancellationToken cancellationToken = default
    )
    {
        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("documentVersion", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("fileName", SearchFieldDataType.String),
            new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnLucene },
            new SearchField(
                "pageNumbers",
                SearchFieldDataType.Collection(SearchFieldDataType.Int32)
            )
            {
                IsFilterable = true,
            },
            new SearchableField("sectionTitle")
            {
                IsFilterable = false,
                IsSortable = false,
                IsFacetable = false,
            },
            new SearchField(
                "contentVector",
                SearchFieldDataType.Collection(SearchFieldDataType.Single)
            )
            {
                IsSearchable = true,
                IsHidden = true,
                VectorSearchDimensions = 1536,
                VectorSearchProfileName = "policywise-vector-profile",
            },
            new SimpleField("policyType", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("blobPath", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("chunkIndex", SearchFieldDataType.Int32) { IsSortable = true },
            new SimpleField("chunkingStrategyVersion", SearchFieldDataType.String)
            {
                IsFilterable = true,
            },
        };

        var index = new SearchIndex(indexName)
        {
            Fields = fields,
            VectorSearch = new VectorSearch
            {
                Algorithms =
                {
                    new HnswAlgorithmConfiguration("policywise-hnsw")
                    {
                        Parameters = new HnswParameters
                        {
                            Metric = VectorSearchAlgorithmMetric.Cosine,
                        },
                    },
                },
                Profiles =
                {
                    new VectorSearchProfile("policywise-vector-profile", "policywise-hnsw"),
                },
            },
        };

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
    }
}
