using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Chunking.Citations;

public interface IPageNumberResolver
{
    IReadOnlyList<int> Resolve(
        IReadOnlyList<ContentSpan> sourceSpans,
        IReadOnlyList<ExtractedPage> pages
    );
}
