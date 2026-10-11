using Azure;
using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Azure.Storage.Blobs;
using PolicyWise.Application.Chunking;
using PolicyWise.Application.Chunking.Citations;
using PolicyWise.Application.Chunking.Splitting;
using PolicyWise.Application.Documents;
using PolicyWise.Application.Extraction;
using PolicyWise.Infrastructure.Extraction;
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

            await using var stream = await blobReader.OpenReadAsync(blobPath, cancellationToken);

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
}

app.Run();
