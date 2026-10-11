using PolicyWise.Application.Chunking.Splitting;

namespace PolicyWise.Application.Tests.Chunking;

public sealed class ContentBlockSplitterTests
{
    private readonly ContentBlockSplitter _splitter = new();

    [Fact]
    public void Split_ShortParagraph_ReturnsSinglePiece()
    {
        // Arrange
        var content = "Windscreen repair is covered.";

        // Act
        var pieces = _splitter.Split(content, 0, 150);

        // Assert
        Assert.Single(pieces);
        Assert.Equal(content, pieces[0].Text);

        Assert.Single(pieces[0].SourceSpans);
        Assert.Equal(0, pieces[0].SourceSpans[0].Offset);
        Assert.Equal(content.Length, pieces[0].SourceSpans[0].Length);
    }

    [Fact]
    public void Split_LongParagraph_ReturnsMultiplePieces()
    {
        // Arrange
        var content = string.Join(" ", Enumerable.Repeat("Windscreen replacement is covered.", 30));

        // Act
        var pieces = _splitter.Split(content, 0, 150);

        // Assert
        Assert.True(pieces.Count > 1);
        Assert.All(pieces, piece => Assert.True(piece.Text.Length <= 150));

        Assert.Equal(content, string.Join(" ", pieces.Select(p => p.Text)));
    }

    [Fact]
    public void Split_OversizedTable_ThrowsUntilRowMappingIsImplemented()
    {
        var table = """
            <table>
            <tr><th>Cover</th><th>Excess</th></tr>
            <tr><td>Windscreen</td><td>£100</td></tr>
            <tr><td>Theft</td><td>£250</td></tr>
            <tr><td>Fire</td><td>£300</td></tr>
            </table>
            """;

        Assert.Throws<NotSupportedException>(() => _splitter.Split(table, 0, 125));
    }

    [Fact]
    public void Split_LongParagraph_PreservesOriginalWordPositions()
    {
        var content = "Windscreen repairs are covered. " + "Replacement requires £100 excess.";

        const int sourceOffset = 500;

        var pieces = _splitter.Split(content, sourceOffset, 35);

        Assert.True(pieces.Count > 1);

        foreach (var piece in pieces)
        {
            foreach (var span in piece.SourceSpans)
            {
                var originalText = content.Substring(span.Offset - sourceOffset, span.Length);

                Assert.Contains(originalText, piece.Text, StringComparison.Ordinal);
            }
        }
    }
}
