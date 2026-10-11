using System.Text;
using System.Text.RegularExpressions;
using PolicyWise.Application.Chunking.Citations;
using PolicyWise.Application.Chunking.Models;
using PolicyWise.Application.Chunking.Splitting;
using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Chunking;

public sealed class StructureAwareDocumentChunker : IDocumentChunker
{
    private readonly ChunkingOptions _options;
    private readonly IContentBlockSplitter _splitter;
    private readonly IPageNumberResolver _pageNumberResolver;

    public StructureAwareDocumentChunker(
        ChunkingOptions options,
        IContentBlockSplitter splitter,
        IPageNumberResolver pageNumberResolver
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(splitter);

        if (options.MaxCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxCharacters));

        if (string.IsNullOrWhiteSpace(options.StrategyVersion))
            throw new ArgumentException("Strategy version is required.");

        _options = options;
        _splitter = splitter;
        _pageNumberResolver = pageNumberResolver;
    }

    // 1. Main orchestration
    public IReadOnlyList<DocumentChunk> Chunk(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.SourceVersion))
            throw new ArgumentException("Source version is required.");

        var blocks = ParseBlocks(document.Markdown);
        var chunks = new List<DocumentChunk>();

        var headings = new SortedDictionary<int, string>();
        var currentContent = new StringBuilder();
        var currentSourceSpans = new List<ContentSpan>();
        var hasBodyContent = false;

        foreach (var block in blocks)
        {
            // A new heading starts a new section.
            if (block.IsHeading)
            {
                SaveCurrentChunk();

                UpdateHeadingHierarchy(headings, block.Text);
                currentContent.Append(GetHeadingContext(headings));

                continue;
            }

            if (string.IsNullOrWhiteSpace(block.Text))
                continue;

            var headingContext = GetHeadingContext(headings);

            // Reserve space for the heading context and separators.
            var availableCharacters = _options.MaxCharacters - headingContext.Length - 2;

            if (availableCharacters <= 0)
            {
                throw new InvalidOperationException(
                    "Heading context exceeds the configured chunk size."
                );
            }

            // Split the block into pieces that fit the available budget.
            var pieces = _splitter.Split(block.Text, block.SourceSpan.Offset, availableCharacters);

            foreach (var piece in pieces)
            {
                var pieceText = piece.Text;
                var separatorLength = currentContent.Length > 0 ? 2 : 0;

                if (
                    hasBodyContent
                    && currentContent.Length + separatorLength + pieceText.Length
                        > _options.MaxCharacters
                )
                {
                    SaveCurrentChunk();
                    currentContent.Append(headingContext);
                }

                if (currentContent.Length > 0)
                    currentContent.AppendLine().AppendLine();

                currentContent.Append(pieceText);
                currentSourceSpans.AddRange(piece.SourceSpans);
                hasBodyContent = true;
            }
        }

        // Save the final chunk.
        SaveCurrentChunk();

        return chunks;

        // Local helper: uses the current document and heading context.

        void SaveCurrentChunk()
        {
            if (!hasBodyContent)
            {
                currentContent.Clear();
                currentSourceSpans.Clear();
                return;
            }

            var pageNumbers = _pageNumberResolver.Resolve(currentSourceSpans, document.Pages);

            chunks.Add(
                CreateChunk(
                    document,
                    chunks.Count,
                    GetSectionTitle(headings),
                    currentContent.ToString().Trim(),
                    pageNumbers
                )
            );

            currentContent.Clear();
            currentSourceSpans.Clear();
            hasBodyContent = false;
        }
    }

    // 2. Create a chunk with stable, version-aware identity
    private DocumentChunk CreateChunk(
        ExtractedDocument document,
        int chunkIndex,
        string sectionTitle,
        string content,
        IReadOnlyList<int> pageNumbers
    )
    {
        return new DocumentChunk(
            ChunkId: ChunkIdGenerator.Generate(
                document.DocumentId,
                document.SourceVersion,
                _options.StrategyVersion,
                chunkIndex
            ),
            DocumentId: document.DocumentId,
            PolicyType: document.PolicyType,
            BlobPath: document.BlobPath,
            SectionTitle: sectionTitle,
            Content: content,
            PageNumbers: pageNumbers,
            ChunkIndex: chunkIndex,
            SourceVersion: document.SourceVersion,
            ChunkingStrategyVersion: _options.StrategyVersion
        );
    }

    // 3. Maintain the heading hierarchy
    private static void UpdateHeadingHierarchy(
        SortedDictionary<int, string> headings,
        string heading
    )
    {
        var level = heading.TakeWhile(c => c == '#').Count();
        var title = heading.TrimStart('#', ' ').Trim();

        // Remove headings at the same or deeper level.
        foreach (var key in headings.Keys.Where(k => k >= level).ToList())
        {
            headings.Remove(key);
        }

        headings[level] = title;
    }

    // 4. Build heading metadata and context
    private static string GetSectionTitle(SortedDictionary<int, string> headings)
    {
        return headings.Count == 0 ? "Introduction" : string.Join(" → ", headings.Values);
    }

    private static string GetHeadingContext(SortedDictionary<int, string> headings)
    {
        return string.Join("\n", headings.Select(h => $"{new string('#', h.Key)} {h.Value}"));
    }

    // 5. Convert Markdown into structural blocks

    private static IReadOnlyList<MarkdownBlock> ParseBlocks(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var blocks = new List<MarkdownBlock>();

        // Match complete lines while retaining their original offsets.
        var lines = Regex
            .Matches(markdown, @"[^\r\n]*(?:\r\n|\n|\r|$)")
            .Where(match => match.Length > 0)
            .ToList();

        var paragraph = new StringBuilder();
        var table = new StringBuilder();

        var paragraphStart = -1;
        var paragraphEnd = -1;

        var tableStart = -1;
        var tableEnd = -1;
        var insideTable = false;

        foreach (var line in lines)
        {
            var text = line.Value.Trim();
            var lineStart = line.Index;
            var lineEnd = line.Index + line.Length;

            // 1. Detect the beginning of an HTML table.
            if (!insideTable && text.StartsWith("<table", StringComparison.OrdinalIgnoreCase))
            {
                FlushParagraph();
                insideTable = true;
                tableStart = lineStart;
            }

            // 2. Preserve the complete table as one block.
            if (insideTable)
            {
                table.Append(line.Value);
                tableEnd = lineEnd;

                if (text.Contains("</table>", StringComparison.OrdinalIgnoreCase))
                {
                    FlushTable();
                    insideTable = false;
                }

                continue;
            }

            // 3. Detect Markdown headings.
            if (Regex.IsMatch(text, @"^#{1,6}\s+"))
            {
                FlushParagraph();

                blocks.Add(
                    new MarkdownBlock(text, true, new ContentSpan(lineStart, lineEnd - lineStart))
                );

                continue;
            }

            // 4. Blank lines separate paragraphs.
            if (string.IsNullOrWhiteSpace(text))
            {
                FlushParagraph();
                continue;
            }

            // 5. Accumulate normal paragraph lines.
            if (paragraphStart < 0)
                paragraphStart = lineStart;

            paragraph.Append(line.Value);
            paragraphEnd = lineEnd;
        }

        FlushParagraph();

        if (insideTable)
            FlushTable();

        return blocks;

        void FlushParagraph()
        {
            if (paragraphStart < 0)
                return;

            blocks.Add(
                new MarkdownBlock(
                    paragraph.ToString().Trim(),
                    false,
                    new ContentSpan(paragraphStart, paragraphEnd - paragraphStart)
                )
            );

            paragraph.Clear();
            paragraphStart = -1;
            paragraphEnd = -1;
        }

        void FlushTable()
        {
            if (tableStart < 0)
                return;

            blocks.Add(
                new MarkdownBlock(
                    table.ToString().Trim(),
                    false,
                    new ContentSpan(tableStart, tableEnd - tableStart)
                )
            );

            table.Clear();
            tableStart = -1;
            tableEnd = -1;
        }
    }

    private sealed record MarkdownBlock(string Text, bool IsHeading, ContentSpan SourceSpan);
}
