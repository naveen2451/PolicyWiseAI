using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Chunking.Models;

public sealed record ContentPiece(string Text, IReadOnlyList<ContentSpan> SourceSpans);
