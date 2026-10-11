using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using PolicyWise.Application.Extraction;
using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Infrastructure.Extraction;

public sealed class DocumentIntelligenceTextExtractor : IDocumentTextExtractor
{
    private readonly DocumentIntelligenceClient _client;

    public DocumentIntelligenceTextExtractor(IConfiguration configuration)
    {
        var endpoint =
            configuration["Azure:DocumentIntelligence:Endpoint"]
            ?? throw new InvalidOperationException("Document Intelligence endpoint is missing.");

        _client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureCliCredential());
    }

    public async Task<ExtractedDocument> ExtractAsync(
        string documentId,
        string blobPath,
        string policyType,
        Stream documentStream,
        CancellationToken cancellationToken = default
    )
    {
        using var memoryStream = new MemoryStream();

        await documentStream.CopyToAsync(memoryStream, cancellationToken);

        var options = new AnalyzeDocumentOptions(
            "prebuilt-layout",
            BinaryData.FromBytes(memoryStream.ToArray())
        )
        {
            OutputContentFormat = DocumentContentFormat.Markdown,
        };

        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            options,
            cancellationToken
        );

        var result = operation.Value;

        var pages = result
            .Pages.Select(page => new ExtractedPage(
                page.PageNumber,
                page.Spans.Select(span => new ContentSpan(span.Offset, span.Length)).ToList()
            ))
            .ToList();

        return new ExtractedDocument(documentId, blobPath, policyType, result.Content, pages);
    }
}
