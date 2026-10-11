using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Chunking.Citations;

public sealed class PageNumberResolver : IPageNumberResolver
{
    public IReadOnlyList<int> Resolve(
        IReadOnlyList<ContentSpan> sourceSpans,
        IReadOnlyList<ExtractedPage> pages
    )
    {
        ArgumentNullException.ThrowIfNull(sourceSpans);
        ArgumentNullException.ThrowIfNull(pages);

        if (sourceSpans.Count == 0 || pages.Count == 0)
            return [];

        return pages
            .Where(page =>
                page.Spans.Any(pageSpan =>
                    sourceSpans.Any(chunkSpan => Overlaps(chunkSpan, pageSpan))
                )
            )
            .Select(page => page.PageNumber)
            .Distinct()
            .OrderBy(pageNumber => pageNumber)
            .ToArray();
    }

    private static bool Overlaps(ContentSpan first, ContentSpan second)
    {
        if (first.Length <= 0 || second.Length <= 0)
            return false;

        var firstEnd = (long)first.Offset + first.Length;
        var secondEnd = (long)second.Offset + second.Length;

        return first.Offset < secondEnd && second.Offset < firstEnd;
    }
}
