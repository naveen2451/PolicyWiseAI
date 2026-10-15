using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Azure.Storage.Blobs;
using PolicyWise.Api.Models;
using PolicyWise.Application.Answers;
using PolicyWise.Application.Answers.Verification;
using PolicyWise.Application.Chunking;
using PolicyWise.Application.Chunking.Citations;
using PolicyWise.Application.Chunking.Splitting;
using PolicyWise.Application.Documents;
using PolicyWise.Application.Embeddings;
using PolicyWise.Application.Evaluation;
using PolicyWise.Application.Evidence;
using PolicyWise.Application.Extraction;
using PolicyWise.Application.Search;
using PolicyWise.Infrastructure.AI;
using PolicyWise.Infrastructure.Embeddings;
using PolicyWise.Infrastructure.Evaluation;
using PolicyWise.Infrastructure.Extraction;
using PolicyWise.Infrastructure.Search;
using PolicyWise.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

var storageUri =
    builder.Configuration["Azure:Storage:ServiceUri"]
    ?? throw new InvalidOperationException("Azure Storage URI is missing.");

builder.Services.AddSingleton(new BlobServiceClient(new Uri(storageUri), new AzureCliCredential()));

var searchEndpoint =
    builder.Configuration["Azure:Search:Endpoint"]
    ?? throw new InvalidOperationException("Azure Search endpoint is missing.");

builder.Services.AddSingleton(
    new SearchIndexClient(new Uri(searchEndpoint), new AzureCliCredential())
);

builder.Services.AddScoped<IBlobDocumentReader, BlobDocumentReader>();

builder.Services.AddScoped<IDocumentTextExtractor, DocumentIntelligenceTextExtractor>();

builder.Services.AddSingleton(
    new ChunkingOptions { MaxCharacters = 1500, StrategyVersion = "structure-v3-max1500" }
);

builder.Services.AddSingleton<IDocumentChunker, StructureAwareDocumentChunker>();

builder.Services.AddSingleton<IContentBlockSplitter, ContentBlockSplitter>();

builder.Services.AddSingleton<IPageNumberResolver, PageNumberResolver>();

builder.Services.AddSingleton<ITextEmbeddingGenerator>(sp =>
{
    Console.WriteLine("1. Creating embedding generator");

    var configuration = sp.GetRequiredService<IConfiguration>();

    var endpoint = configuration["Embeddings:Endpoint"]!;
    var deployment = configuration["Embeddings:DeploymentName"]!;

    Console.WriteLine("2. Configuration loaded");

    var generator = new AzureOpenAIEmbeddingGenerator(endpoint, deployment);

    Console.WriteLine("3. Embedding generator created");

    return generator;
});

builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    var endpoint =
        configuration["Azure:Search:Endpoint"]
        ?? throw new InvalidOperationException("Azure Search endpoint is missing.");

    return new PolicyWiseSearchIndexManager(endpoint);
});

builder.Services.AddSingleton<ISearchDocumentIndexer>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    var endpoint =
        configuration["Azure:Search:Endpoint"]
        ?? throw new InvalidOperationException("Azure Search endpoint missing");

    var indexName =
        configuration["Azure:Search:IndexName"]
        ?? throw new InvalidOperationException("Azure Search index name missing");

    return new AzureSearchDocumentIndexer(endpoint, indexName);
});

builder.Services.AddScoped<DocumentEmbeddingService>();

builder.Services.AddSingleton<IDocumentRetriever>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    var endpoint =
        configuration["Azure:Search:Endpoint"]
        ?? throw new InvalidOperationException("Search endpoint missing");

    var indexName =
        configuration["Azure:Search:IndexName"]
        ?? throw new InvalidOperationException("Search index name missing");

    var logger = sp.GetRequiredService<ILogger<AzureSearchDocumentRetriever>>();

    return new AzureSearchDocumentRetriever(endpoint, indexName, logger);
});

builder.Services.AddScoped<DocumentRetrievalService>();

builder.Services.AddSingleton<IEvidenceValidator>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    var endpoint =
        configuration["EvidenceValidation:Endpoint"]
        ?? throw new InvalidOperationException("Evidence validation endpoint is missing.");

    var deployment =
        configuration["EvidenceValidation:DeploymentName"]
        ?? throw new InvalidOperationException("Evidence validation deployment is missing.");

    var logger = sp.GetRequiredService<ILogger<AzureOpenAiEvidenceValidator>>();

    return new AzureOpenAiEvidenceValidator(endpoint, deployment, logger);
});

builder.Services.AddSingleton<IAnswerGenerator>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    var endpoint =
        configuration["EvidenceValidation:Endpoint"]
        ?? throw new InvalidOperationException("Azure OpenAI endpoint is missing.");

    var deployment =
        configuration["EvidenceValidation:DeploymentName"]
        ?? throw new InvalidOperationException("Azure OpenAI chat deployment is missing.");

    var logger = sp.GetRequiredService<ILogger<AzureOpenAiAnswerGenerator>>();

    return new AzureOpenAiAnswerGenerator(endpoint, deployment, logger);
});

builder.Services.AddScoped<PolicyQuestionAnswerService>();

builder.Services.AddSingleton<IAnswerGroundingVerifier>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    var endpoint =
        configuration["EvidenceValidation:Endpoint"]
        ?? throw new InvalidOperationException("Azure OpenAI endpoint is missing.");

    var deployment =
        configuration["EvidenceValidation:DeploymentName"]
        ?? throw new InvalidOperationException("Azure OpenAI deployment is missing.");

    var logger = sp.GetRequiredService<ILogger<AzureOpenAiAnswerGroundingVerifier>>();

    return new AzureOpenAiAnswerGroundingVerifier(endpoint, deployment, logger);
});

builder.Services.AddHttpClient<IRagEvaluationService, FoundryRagEvaluationService>();

builder.Services.AddSingleton<TokenCredential>(_ => new AzureCliCredential());
var app = builder.Build();

// Simple routing test
app.MapGet(
    "/api/health/ping",
    () =>
    {
        return Results.Ok(new { Status = "Healthy", Message = "PolicyWise API is running" });
    }
);

// Azure Blob Storage connectivity test
app.MapGet(
    "/api/health/blob",
    async (
        BlobServiceClient blobServiceClient,
        IConfiguration configuration,
        CancellationToken cancellationToken
    ) =>
    {
        var containerName = configuration["Azure:Storage:ContainerName"];

        if (string.IsNullOrWhiteSpace(containerName))
        {
            return Results.Problem("Container name is missing.", statusCode: 500);
        }

        try
        {
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            var exists = await containerClient.ExistsAsync(cancellationToken);

            if (!exists.Value)
            {
                return Results.NotFound(
                    new { Status = "Unhealthy", Message = "Container not found" }
                );
            }

            return Results.Ok(
                new
                {
                    Status = "Healthy",
                    Service = "Azure Blob Storage",
                    Container = containerName,
                }
            );
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Blob Storage connectivity failed");

            return Results.Problem("Blob Storage connectivity failed.", statusCode: 503);
        }
    }
);
app.MapGet(
    "/api/health/search",
    async (
        SearchIndexClient searchClient,
        ILogger<Program> logger,
        CancellationToken cancellationToken
    ) =>
    {
        try
        {
            // Lists indexes to verify authentication and service connectivity.
            var indexes = new List<string>();

            await foreach (var index in searchClient.GetIndexesAsync(cancellationToken))
            {
                indexes.Add(index.Name);
            }

            return Results.Ok(
                new
                {
                    Status = "Healthy",
                    Service = "Azure AI Search",
                    IndexCount = indexes.Count,
                    Indexes = indexes,
                }
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Azure AI Search connectivity failed.");

            return Results.Problem(detail: "Azure AI Search connectivity failed.", statusCode: 503);
        }
    }
);

if (app.Environment.IsDevelopment())
{
    app.MapGet(
        "/api/dev/documents",
        async (IBlobDocumentReader documentReader, CancellationToken cancellationToken) =>
        {
            var documents = await documentReader.ListDocumentsAsync(cancellationToken);

            return Results.Ok(new { TotalDocuments = documents.Count, Documents = documents });
        }
    );

    app.MapGet(
        "/api/dev/documents/analyze",
        async (
            string blobPath,
            IBlobDocumentReader blobReader,
            IDocumentTextExtractor extractor,
            CancellationToken cancellationToken
        ) =>
        {
            if (
                !blobPath.StartsWith("car/", StringComparison.OrdinalIgnoreCase)
                && !blobPath.StartsWith("life/", StringComparison.OrdinalIgnoreCase)
            )
            {
                return Results.BadRequest("Invalid document path.");
            }

            if (!blobPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Only PDFs are supported.");
            }

            await using var stream = await blobReader.OpenReadAsync(blobPath, cancellationToken);

            var policyType = blobPath.Split('/')[0];

            var result = await extractor.ExtractAsync(
                documentId: blobPath,
                blobPath: blobPath,
                policyType: policyType,
                documentStream: stream,
                cancellationToken: cancellationToken
            );

            return Results.Ok(result);
        }
    );

    app.MapGet(
        "/api/dev/documents/chunks",
        async (
            string blobPath,
            IBlobDocumentReader blobReader,
            IDocumentTextExtractor extractor,
            IDocumentChunker chunker,
            CancellationToken cancellationToken
        ) =>
        {
            if (
                !blobPath.StartsWith("car/", StringComparison.OrdinalIgnoreCase)
                && !blobPath.StartsWith("life/", StringComparison.OrdinalIgnoreCase)
            )
            {
                return Results.BadRequest("Invalid document path.");
            }

            if (!blobPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Only PDFs are supported.");
            }

            await using var blobStream = await blobReader.OpenReadAsync(
                blobPath,
                cancellationToken
            );

            // Use one seekable in-memory copy for this small PoC.
            using var documentStream = new MemoryStream();

            await blobStream.CopyToAsync(documentStream, cancellationToken);

            documentStream.Position = 0;

            var sourceVersion = await DocumentVersionCalculator.CalculateAsync(
                documentStream,
                cancellationToken
            );

            var extractedDocument = await extractor.ExtractAsync(
                documentId: blobPath,
                blobPath: blobPath,
                policyType: blobPath.Split('/')[0],
                documentStream: documentStream,
                cancellationToken: cancellationToken
            );

            var versionedDocument = extractedDocument with { SourceVersion = sourceVersion };

            var chunks = chunker.Chunk(versionedDocument);

            return Results.Ok(
                new
                {
                    DocumentId = extractedDocument.DocumentId,
                    MarkdownLength = extractedDocument.Markdown.Length,

                    Pages = extractedDocument.Pages.Select(page => new
                    {
                        page.PageNumber,
                        Spans = page.Spans.Select(span => new { span.Offset, span.Length }),
                    }),

                    Chunks = chunks.Select(chunk => new
                    {
                        chunk.ChunkIndex,
                        chunk.SectionTitle,
                        chunk.PageNumbers,
                        ContentPreview = chunk.Content.Length > 200
                            ? chunk.Content[..200]
                            : chunk.Content,
                    }),
                }
            );
        }
    );
    app.MapPost(
        "/api/dev/embeddings/test",
        async (
            EmbeddingTestRequest request,
            ITextEmbeddingGenerator generator,
            CancellationToken cancellationToken
        ) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
                return Results.BadRequest("Text is required.");

            Console.WriteLine("4. Calling embedding model");

            var embedding = await generator.GenerateAsync(request.Text, cancellationToken);

            Console.WriteLine($"5. Embedding received: {embedding.Length}");

            return Results.Ok(
                new
                {
                    Dimensions = embedding.Length,
                    FirstFiveValues = embedding.Span[..Math.Min(5, embedding.Length)].ToArray(),
                }
            );
        }
    );
    app.MapPost(
        "/api/dev/embeddings/compare",
        async (ITextEmbeddingGenerator generator, CancellationToken ct) =>
        {
            var query = "Does my car insurance cover a broken windscreen?";

            var documents = new[]
            {
                "The policy covers accidental damage to the vehicle's windscreen.",
                "The insurer will pay for broken or cracked car glass.",
                "Life insurance provides a lump sum payment upon death.",
            };

            var queryVector = await generator.GenerateAsync(query, ct);

            var results = new List<object>();

            foreach (var document in documents)
            {
                var documentVector = await generator.GenerateAsync(document, ct);

                var score = CosineSimilarity.Calculate(queryVector.Span, documentVector.Span);

                results.Add(new { Document = document, Similarity = Math.Round(score, 4) });
            }

            return Results.Ok(results);
        }
    );

    app.MapPost(
        "/api/dev/search/create-index",
        async (
            PolicyWiseSearchIndexManager indexManager,
            IConfiguration configuration,
            CancellationToken ct
        ) =>
        {
            var indexName =
                configuration["Azure:Search:IndexName"]
                ?? throw new InvalidOperationException("Search index name is missing.");

            await indexManager.CreateIndexAsync(indexName, ct);

            return Results.Ok(
                new { Message = "Azure AI Search index created or updated", IndexName = indexName }
            );
        }
    );
    app.MapPost(
        "/api/dev/documents/ingest",
        async (
            string blobPath,
            IBlobDocumentReader blobReader,
            IDocumentTextExtractor extractor,
            IDocumentChunker chunker,
            DocumentEmbeddingService embeddingService,
            CancellationToken cancellationToken
        ) =>
        {
            // 1. Validate the blob path
            if (
                !blobPath.StartsWith("car/", StringComparison.OrdinalIgnoreCase)
                && !blobPath.StartsWith("life/", StringComparison.OrdinalIgnoreCase)
            )
            {
                return Results.BadRequest("Invalid document path.");
            }

            if (!blobPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Only PDFs are supported.");
            }

            // 2. Read the PDF from Azure Blob Storage
            await using var blobStream = await blobReader.OpenReadAsync(
                blobPath,
                cancellationToken
            );

            using var documentStream = new MemoryStream();

            await blobStream.CopyToAsync(documentStream, cancellationToken);

            documentStream.Position = 0;

            // 3. Calculate source document version
            var sourceVersion = await DocumentVersionCalculator.CalculateAsync(
                documentStream,
                cancellationToken
            );

            // Ensure the extractor reads from the beginning
            documentStream.Position = 0;

            // 4. Extract Markdown using Document Intelligence
            var extractedDocument = await extractor.ExtractAsync(
                documentId: blobPath,
                blobPath: blobPath,
                policyType: blobPath.Split('/')[0],
                documentStream: documentStream,
                cancellationToken: cancellationToken
            );

            // 5. Attach the document version
            var versionedDocument = extractedDocument with
            {
                SourceVersion = sourceVersion,
            };

            // 6. Generate document chunks
            var chunks = chunker.Chunk(versionedDocument);

            // 7. Generate embeddings and upload to Azure AI Search
            var indexedCount = await embeddingService.ProcessAsync(chunks, cancellationToken);

            // 8. Return ingestion summary
            return Results.Ok(
                new
                {
                    DocumentId = extractedDocument.DocumentId,
                    BlobPath = blobPath,
                    SourceVersion = sourceVersion,
                    TotalPages = extractedDocument.Pages.Count(),
                    TotalChunks = chunks.Count,
                    IndexedChunks = indexedCount,
                    Message = "Document indexed successfully",
                }
            );
        }
    );
    app.MapGet(
        "/api/dev/search/vector",
        async (
            string question,
            int? topK,
            string? policyType,
            DocumentRetrievalService retrievalService,
            CancellationToken cancellationToken
        ) =>
        {
            if (string.IsNullOrWhiteSpace(question))
                return Results.BadRequest("Question is required.");

            if (topK is < 1 or > 50)
                return Results.BadRequest("topK must be between 1 and 50.");

            var results = await retrievalService.SearchAsync(
                question,
                topK ?? 5,
                policyType,
                cancellationToken
            );

            return Results.Ok(
                new
                {
                    Question = question,
                    SearchType = "Vector",
                    TotalResults = results.Count,
                    Results = results.Select(
                        (result, index) =>
                            new
                            {
                                Rank = index + 1,
                                result.Score,
                                result.DocumentId,
                                result.PolicyType,
                                result.SectionTitle,
                                result.PageNumbers,
                                result.Content,
                            }
                    ),
                }
            );
        }
    );
    app.MapGet(
        "/api/dev/evidence/validate",
        async (
            string question,
            string? policyType,
            int? topK,
            DocumentRetrievalService retrievalService,
            IEvidenceValidator evidenceValidator,
            CancellationToken cancellationToken
        ) =>
        {
            if (string.IsNullOrWhiteSpace(question))
                return Results.BadRequest("Question is required.");

            if (topK is < 1 or > 20)
                return Results.BadRequest("topK must be between 1 and 20.");

            var stopwatch = Stopwatch.StartNew();

            // Step 1: Retrieve candidate chunks using HYBRID search.
            var chunks = await retrievalService.SearchAsync(
                question,
                topK ?? 5,
                policyType,
                cancellationToken
            );

            // Step 2: Ask the LLM whether the evidence is sufficient.
            var validation = await evidenceValidator.ValidateAsync(
                question,
                chunks,
                cancellationToken
            );

            stopwatch.Stop();

            return Results.Ok(
                new
                {
                    Question = question,
                    RetrievalMode = "Hybrid",
                    RetrievedChunks = chunks.Count,
                    Validation = new
                    {
                        Status = validation.Status.ToString(),
                        validation.SupportingChunkIds,
                        validation.Reason,
                    },
                    RetrievedEvidence = chunks.Select(
                        (chunk, index) =>
                            new
                            {
                                Rank = index + 1,
                                chunk.ChunkId,
                                chunk.DocumentId,
                                chunk.SectionTitle,
                                chunk.PageNumbers,
                                chunk.Score,
                                IsSupportingEvidence = validation.SupportingChunkIds.Contains(
                                    chunk.ChunkId
                                ),
                            }
                    ),
                    TotalDurationMs = stopwatch.ElapsedMilliseconds,
                }
            );
        }
    );

    app.MapGet(
        "/api/dev/ask",
        async (
            string question,
            string? policyType,
            int? topK,
            PolicyQuestionAnswerService answerService,
            CancellationToken cancellationToken
        ) =>
        {
            if (string.IsNullOrWhiteSpace(question))
                return Results.BadRequest("Question is required.");

            if (topK is < 1 or > 20)
                return Results.BadRequest("topK must be between 1 and 20.");

            var result = await answerService.AskAsync(
                question,
                policyType,
                topK ?? 5,
                cancellationToken
            );

            return Results.Ok(result);
        }
    );
    //custom groundness
    app.MapPost(
        "/api/dev/grounding/verify",
        async (
            GroundingTestRequest request,
            DocumentRetrievalService retrievalService,
            IAnswerGroundingVerifier groundingVerifier,
            CancellationToken cancellationToken
        ) =>
        {
            if (
                string.IsNullOrWhiteSpace(request.Question)
                || string.IsNullOrWhiteSpace(request.Answer)
            )
            {
                return Results.BadRequest("Question and answer are required.");
            }

            if (request.TopK is < 1 or > 20)
            {
                return Results.BadRequest("TopK must be between 1 and 20.");
            }

            // Retrieve real policy evidence using hybrid search.
            var chunks = await retrievalService.SearchAsync(
                request.Question,
                request.TopK,
                request.PolicyType,
                cancellationToken
            );

            // Use only the explicitly selected chunk as cited evidence.
            var citedChunks = chunks
                .Where(chunk => chunk.ChunkId == request.CitedChunkId)
                .ToArray();

            if (citedChunks.Length == 0)
            {
                return Results.BadRequest("CitedChunkId must match a chunk returned by retrieval.");
            }

            var verification = await groundingVerifier.VerifyAsync(
                request.Question,
                request.Answer,
                citedChunks,
                cancellationToken
            );

            return Results.Ok(
                new
                {
                    verification.Status,
                    verification.UnsupportedClaims,
                    verification.Reason,
                    CitedEvidence = citedChunks.Select(chunk => new
                    {
                        chunk.ChunkId,
                        chunk.DocumentId,
                        chunk.SectionTitle,
                        chunk.PageNumbers,
                    }),
                }
            );
        }
    );
    //Foundary Groundness

    app.MapGet(
        "/api/dev/foundry/configuration",
        async (IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var endpoint = configuration["FoundryEvaluation:ProjectEndpoint"];

            var evaluationId = configuration["FoundryEvaluation:EvaluationId"];

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(evaluationId))
            {
                return Results.BadRequest("Foundry project endpoint or evaluation ID is missing.");
            }

            var credential = new AzureCliCredential();

            var accessToken = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://ai.azure.com/.default" }),
                cancellationToken
            );

            using var client = new HttpClient();

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                accessToken.Token
            );

            var url =
                $"{endpoint.TrimEnd('/')}/openai/v1/evals/" + Uri.EscapeDataString(evaluationId);

            //var url = $"{endpoint.TrimEnd('/')}/openai/v1/evals?limit=100";

            using var response = await client.GetAsync(url, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            Console.WriteLine("=== FOUNDRY EVALUATION CONFIGURATION ===");

            // Development only. Inspect before sharing externally.
            Console.WriteLine(responseBody);

            return Results.Content(
                responseBody,
                "application/json",
                statusCode: (int)response.StatusCode
            );
        }
    );

    app.MapPost(
        "/api/dev/foundry/evaluate",
        async (
            FoundryEvaluationRequest request,
            IRagEvaluationService evaluator,
            CancellationToken cancellationToken
        ) =>
        {
            var result = await evaluator.SubmitAsync(
                new RagEvaluationInput(request.Query, request.Context, request.Response),
                cancellationToken
            );

            return Results.Ok(result);
        }
    );
    app.MapGet(
        "/api/dev/foundry/evaluate/{runId}",
        async (
            string runId,
            IRagEvaluationService evaluator,
            CancellationToken cancellationToken
        ) =>
        {
            var result = await evaluator.GetResultAsync(runId, cancellationToken);

            var metrics = FoundryEvaluationResultParser.Parse(result);

            return Results.Ok(metrics);
        }
    );
}
app.Run();

public sealed record GroundingTestRequest(
    string Question,
    string Answer,
    string CitedChunkId,
    string? PolicyType = "car",
    int TopK = 5
);

public sealed record FoundryEvaluationRequest(string Query, string Context, string Response);
