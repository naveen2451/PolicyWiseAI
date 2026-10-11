namespace PolicyWise.Application.Extraction.Models;

public sealed record ExtractedDocument(
    string DocumentId,
    string BlobPath,
    string PolicyType,
    string Markdown,
    IReadOnlyList<ExtractedPage> Pages,
    string SourceVersion = ""
);