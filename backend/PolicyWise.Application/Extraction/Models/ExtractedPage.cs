
namespace PolicyWise.Application.Extraction.Models;

public sealed record ExtractedPage(
    int PageNumber,
    IReadOnlyList<ContentSpan> Spans
);

// We deliberately don't split the Markdown into pages yet.
// Why? A table can span multiple pages, and simply dividing the Markdown by page could damage its structure.