using PolicyWise.Application.Chunking.Models;
using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Chunking.Citations;

public static class SourceSpanMapper
{
    public static IReadOnlyList<ContentPiece> Map(
        string originalBlock,
        int sourceOffset,
        IReadOnlyList<string> pieces
    )
    {
        var results = new List<ContentPiece>();
        var searchPosition = 0;

        foreach (var piece in pieces)
        {
            var index = originalBlock.IndexOf(piece, searchPosition, StringComparison.Ordinal);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    "Unable to map split content to its original source."
                );
            }

            results.Add(
                new ContentPiece(piece, [new ContentSpan(sourceOffset + index, piece.Length)])
            );

            searchPosition = index + piece.Length;
        }

        return results;
    }
}
