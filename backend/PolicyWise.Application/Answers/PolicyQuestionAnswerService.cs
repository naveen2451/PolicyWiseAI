using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PolicyWise.Application.Answers.Models;
using PolicyWise.Application.Evidence;
using PolicyWise.Application.Search;

namespace PolicyWise.Application.Answers;

public sealed class PolicyQuestionAnswerService
{
    private readonly DocumentRetrievalService _retrievalService;
    private readonly IEvidenceValidator _evidenceValidator;
    private readonly IAnswerGenerator _answerGenerator;
    private readonly ILogger<PolicyQuestionAnswerService> _logger;

    public PolicyQuestionAnswerService(
        DocumentRetrievalService retrievalService,
        IEvidenceValidator evidenceValidator,
        IAnswerGenerator answerGenerator,
        ILogger<PolicyQuestionAnswerService> logger
    )
    {
        _retrievalService = retrievalService;
        _evidenceValidator = evidenceValidator;
        _answerGenerator = answerGenerator;
        _logger = logger;
    }

    public async Task<PolicyAnswerResult> AskAsync(
        string question,
        string? policyType = null,
        int topK = 5,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question is required.", nameof(question));

        if (topK is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(topK));

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "PolicyQuestionStarted TopK={TopK} PolicyType={PolicyType}",
            topK,
            policyType
        );

        // Step 1: Hybrid retrieval
        var chunks = await _retrievalService.SearchAsync(
            question,
            topK,
            policyType,
            cancellationToken
        );

        // Step 2: Validate retrieved evidence
        var validation = await _evidenceValidator.ValidateAsync(
            question,
            chunks,
            cancellationToken
        );

        // Step 3: Stop if evidence is not sufficient
        if (validation.Status == EvidenceStatus.Insufficient)
        {
            _logger.LogInformation(
                "PolicyQuestionAbstained EvidenceStatus={Status} " + "DurationMs={DurationMs}",
                validation.Status,
                stopwatch.ElapsedMilliseconds
            );

            return new PolicyAnswerResult(
                "I couldn't find sufficient information in the "
                    + "uploaded policy documents to confirm this. "
                    + "Please check the full policy wording or contact "
                    + "your insurer.",
                validation.Status,
                Array.Empty<AnswerCitation>()
            );
        }

        // Step 4: Select only supporting evidence
        var supportingIds = validation.SupportingChunkIds.ToHashSet(StringComparer.Ordinal);

        var validatedChunks = chunks
            .Where(chunk => supportingIds.Contains(chunk.ChunkId))
            .ToArray();

        if (validatedChunks.Length == 0)
            throw new InvalidOperationException(
                "Evidence validation returned no supporting chunks."
            );

        // Step 5: Generate answer
        var generated = await _answerGenerator.GenerateAsync(
            question,
            validatedChunks,
            cancellationToken
        );

        // Step 6: Resolve citations from trusted metadata
        var chunkLookup = validatedChunks.ToDictionary(
            chunk => chunk.ChunkId,
            StringComparer.Ordinal
        );

        var citations = generated
            .CitedChunkIds.Select(chunkId =>
            {
                if (!chunkLookup.TryGetValue(chunkId, out var chunk))
                    throw new InvalidOperationException(
                        "Generated answer references unknown evidence."
                    );

                return new AnswerCitation(chunk.ChunkId, chunk.DocumentId, chunk.PageNumbers);
            })
            .ToArray();

        stopwatch.Stop();

        _logger.LogInformation(
            "PolicyQuestionCompleted EvidenceStatus={Status} "
                + "RetrievedChunks={RetrievedCount} "
                + "ValidatedChunks={ValidatedCount} "
                + "Citations={CitationCount} DurationMs={DurationMs}",
            validation.Status,
            chunks.Count,
            validatedChunks.Length,
            citations.Length,
            stopwatch.ElapsedMilliseconds
        );

        return new PolicyAnswerResult(generated.Answer, validation.Status, citations);
    }
}
