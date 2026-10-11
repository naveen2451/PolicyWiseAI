using PolicyWise.Application.Chunking.Citations;
using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Tests.Chunking;

public sealed class PageNumberResolverTests
{
    private readonly PageNumberResolver _resolver = new();

    [Fact]
    public void Resolve_ChunkWithinOnePage_ReturnsOnePage()
    {
        var pages = CreatePages();

        var result = _resolver.Resolve([new ContentSpan(100, 150)], pages);

        Assert.Equal([1], result);
    }

    [Fact]
    public void Resolve_ChunkAcrossPages_ReturnsBothPages()
    {
        var pages = CreatePages();

        var result = _resolver.Resolve([new ContentSpan(350, 200)], pages);

        Assert.Equal([1, 2], result);
    }

    [Fact]
    public void Resolve_ChunkAtBoundary_DoesNotIncludePreviousPage()
    {
        var pages = CreatePages();

        var result = _resolver.Resolve([new ContentSpan(400, 100)], pages);

        Assert.Equal([2], result);
    }

    [Fact]
    public void Resolve_EmptySourceSpans_ReturnsEmpty()
    {
        var result = _resolver.Resolve([], CreatePages());

        Assert.Empty(result);
    }

    private static IReadOnlyList<ExtractedPage> CreatePages()
    {
        return
        [
            new ExtractedPage(1, [new ContentSpan(0, 400)]),
            new ExtractedPage(2, [new ContentSpan(400, 400)]),
        ];
    }

    [Fact]
    public void Resolve_SpanCrossingTwoPages_ReturnsBothPages()
    {
        var resolver = new PageNumberResolver();

        var pages = new List<ExtractedPage>
        {
            new(1, [new ContentSpan(0, 2403)]),
            new(2, [new ContentSpan(2403, 2483)]),
        };

        // Starts on page 1 and ends on page 2
        var chunkSpans = new List<ContentSpan> { new(2300, 200) };

        var result = resolver.Resolve(chunkSpans, pages);

        Assert.Equal([1, 2], result);
    }

    [Theory]
    [InlineData(2300, 103, 1)] // Ends exactly at 2403
    [InlineData(2403, 100, 2)] // Starts exactly at 2403
    public void Resolve_AtPageBoundary_ReturnsCorrectPage(int offset, int length, int expectedPage)
    {
        var resolver = new PageNumberResolver();

        var pages = new List<ExtractedPage>
        {
            new(1, [new ContentSpan(0, 2403)]),
            new(2, [new ContentSpan(2403, 2483)]),
        };

        var result = resolver.Resolve([new ContentSpan(offset, length)], pages);

        Assert.Equal([expectedPage], result);
    }
}
