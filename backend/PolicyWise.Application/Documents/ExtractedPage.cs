namespace PolicyWise.Application.Documents;

public sealed record ExtractedPage(
    string BlobPath,
    int PageNumber,
    string Text
);