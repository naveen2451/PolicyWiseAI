namespace PolicyWise.Application.Extraction.Models;

public sealed record ContentSpan(
    int Offset,
    int Length
);

// Document Intelligence returns spans identifying where page content appears within the overall extracted document.
// These are useful for mapping chunks back to pages.