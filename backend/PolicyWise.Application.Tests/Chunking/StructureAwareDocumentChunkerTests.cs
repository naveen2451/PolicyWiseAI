using PolicyWise.Application.Chunking;
using PolicyWise.Application.Chunking.Citations;
using PolicyWise.Application.Chunking.Splitting;
using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Tests.Chunking;

public sealed class StructureAwareDocumentChunkerTests
{
    private static StructureAwareDocumentChunker CreateChunker(int maxCharacters = 150)
    {
        return new StructureAwareDocumentChunker(
            new ChunkingOptions
            {
                MaxCharacters = maxCharacters,
                StrategyVersion = "structure-v3-max150",
            },
            new ContentBlockSplitter(),
            new PageNumberResolver()
        );
    }

    private static ExtractedDocument CreateDocument(
        string markdown,
        string sourceVersion = "pdf-hash-v1"
    )
    {
        return new ExtractedDocument(
            DocumentId: "motor-policy",
            BlobPath: "car/motor-policy.pdf",
            PolicyType: "car",
            Markdown: markdown,
            Pages: Array.Empty<ExtractedPage>(),
            SourceVersion: sourceVersion
        );
    }

    [Fact]
    public void Chunk_PreservesHeadingHierarchy()
    {
        var document = CreateDocument(
            """
            # Motor Insurance
            ## Windscreen Cover
            ### Replacement

            Replacement is covered with a £100 excess.
            """
        );

        var chunks = CreateChunker().Chunk(document);

        Assert.Single(chunks);

        Assert.Equal("Motor Insurance → Windscreen Cover → Replacement", chunks[0].SectionTitle);

        Assert.Contains("# Motor Insurance", chunks[0].Content);
        Assert.Contains("## Windscreen Cover", chunks[0].Content);
        Assert.Contains("### Replacement", chunks[0].Content);
    }

    [Fact]
    public void Chunk_SameDocument_ReturnsSameIds()
    {
        var document = CreateDocument(
            """
            # Motor Insurance

            Windscreen repair is covered.
            """
        );

        var chunker = CreateChunker();

        var firstRun = chunker.Chunk(document);
        var secondRun = chunker.Chunk(document);

        Assert.Equal(firstRun.Select(c => c.ChunkId), secondRun.Select(c => c.ChunkId));
    }

    [Fact]
    public void Chunk_NewSourceVersion_ChangesIds()
    {
        var markdown = """
            # Motor Insurance

            Windscreen repair is covered.
            """;

        var chunker = CreateChunker();

        var version1 = chunker.Chunk(CreateDocument(markdown, "pdf-hash-v1"));

        var version2 = chunker.Chunk(CreateDocument(markdown, "pdf-hash-v2"));

        Assert.NotEqual(version1[0].ChunkId, version2[0].ChunkId);
    }

    [Fact]
    public void Chunk_SplitsLongSectionsAndRetainsHeadings()
    {
        var paragraph = string.Join(
            " ",
            Enumerable.Repeat("Windscreen replacement is covered.", 20)
        );

        var document = CreateDocument($"# Motor Insurance\n## Windscreen\n\n{paragraph}");

        var chunks = CreateChunker().Chunk(document);

        Assert.True(chunks.Count > 1);

        Assert.All(
            chunks,
            chunk =>
            {
                Assert.Contains("# Motor Insurance", chunk.Content);
                Assert.Contains("## Windscreen", chunk.Content);
                Assert.True(chunk.Content.Length <= 150);
            }
        );
    }

    [Fact]
    public void Chunk_ParagraphAndTable_PreservesBoth()
    {
        var markdown = """
            # Motor Insurance

            The following excess amounts apply:

            <table>
            <tr><th>Cover</th><th>Excess</th></tr>
            <tr><td>Windscreen</td><td>£100</td></tr>
            </table>

            These amounts are subject to policy conditions.
            """;

        var chunks = CreateChunker(500).Chunk(CreateDocument(markdown));

        var combinedContent = string.Join("\n", chunks.Select(c => c.Content));

        Assert.Contains("The following excess amounts apply", combinedContent);
        Assert.Contains("<table>", combinedContent);
        Assert.Contains("Windscreen", combinedContent);
        Assert.Contains("These amounts are subject", combinedContent);
    }

    [Fact]
    public void Chunk_ResolvesPageNumbersFromSourceSpans()
    {
        var markdown = """
            # Motor Insurance

            Windscreen repair is covered.

            ## Theft Cover

            Theft is covered subject to conditions.
            """;

        var theftStart = markdown.IndexOf("Theft is covered", StringComparison.Ordinal);

        var document = CreateDocument(markdown) with
        {
            Pages =
            [
                new ExtractedPage(1, [new ContentSpan(0, theftStart)]),
                new ExtractedPage(2, [new ContentSpan(theftStart, markdown.Length - theftStart)]),
            ],
        };

        var chunks = CreateChunker().Chunk(document);

        var windscreenChunk = Assert.Single(
            chunks.Where(c => c.Content.Contains("Windscreen repair"))
        );

        var theftChunk = Assert.Single(chunks.Where(c => c.Content.Contains("Theft is covered")));

        Assert.Equal([1], windscreenChunk.PageNumbers);
        Assert.Equal([2], theftChunk.PageNumbers);
    }
}
