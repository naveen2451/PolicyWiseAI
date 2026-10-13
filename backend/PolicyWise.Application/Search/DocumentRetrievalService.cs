using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PolicyWise.Application.Embeddings;
using PolicyWise.Application.Search.Models;

namespace PolicyWise.Application.Search;

public sealed class DocumentRetrievalService
{
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly IDocumentRetriever _retriever;
    private readonly ILogger<DocumentRetrievalService> _logger;

    public DocumentRetrievalService(
        ITextEmbeddingGenerator embeddingGenerator,
        IDocumentRetriever retriever,
        ILogger<DocumentRetrievalService> logger
    )
    {
        _embeddingGenerator = embeddingGenerator;
        _retriever = retriever;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
        string question,
        int topK = 5,
        string? policyType = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be empty.");

        _logger.LogInformation(
            "Retrieval started. QuestionLength={Length}, TopK={TopK}, PolicyType={PolicyType}",
            question.Length,
            topK,
            policyType ?? "ALL"
        );

        var embeddingTimer = Stopwatch.StartNew();

        var queryEmbedding = await _embeddingGenerator.GenerateAsync(question, cancellationToken);

        embeddingTimer.Stop();

        _logger.LogInformation(
            "Query embedding completed. Dimensions={Dimensions}, DurationMs={DurationMs}",
            queryEmbedding.Length,
            embeddingTimer.ElapsedMilliseconds
        );

        var retrievalTimer = Stopwatch.StartNew();

        var results = await _retriever.SearchAsync(
            queryEmbedding,
            topK,
            policyType,
            question,
            cancellationToken
        );

        retrievalTimer.Stop();

        _logger.LogInformation(
            "Retrieval pipeline completed. SearchDurationMs={SearchDurationMs}, TotalDurationMs={TotalDurationMs}",
            retrievalTimer.ElapsedMilliseconds,
            embeddingTimer.ElapsedMilliseconds + retrievalTimer.ElapsedMilliseconds
        );

        return results;
    }
}
