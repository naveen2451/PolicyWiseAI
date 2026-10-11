# PolicyWise AI — Milestone 3: Structure-Aware Chunking & PDF Citations

> **Purpose:** A practical revision guide for the chunking pipeline we built in PolicyWise AI. Revisit this before implementing embeddings, debugging citations, or explaining the architecture in an interview.
>
> **Status:** Core pipeline implemented and tested. Production-hardening items are intentionally parked (see [Remaining work](#13-remaining-work-parked)).

## 1. Where chunking fits in PolicyWise AI

**Application:** React/TypeScript frontend, ASP.NET Core (.NET 10) backend following Clean Architecture; PDFs in Azure Blob Storage; Azure Document Intelligence for extraction; Azure AI Search planned for retrieval.

```text
Insurance PDF (Azure Blob Storage)
    │
    ▼
Azure Document Intelligence (prebuilt-layout, Markdown)
    │
    ▼
ExtractedDocument
    ├── Markdown
    ├── Pages: ExtractedPage(PageNumber, Spans)
    ├── DocumentId / BlobPath / PolicyType
    └── SourceVersion
    │
    ▼
StructureAwareDocumentChunker
    ├── ParseBlocks: headings / paragraphs / HTML tables + source offsets
    ├── ContentBlockSplitter: size-bounded ContentPiece + source spans
    ├── Pack pieces into chunks; repeat heading context
    └── PageNumberResolver: map source spans to PDF pages
    │
    ▼
DocumentChunk[]
    ├── ChunkId / DocumentId / SourceVersion
    ├── SectionTitle / Content / ChunkIndex
    ├── PageNumbers / BlobPath / PolicyType
    └── ChunkingStrategyVersion
    │
    ▼
NEXT: embeddings → Azure AI Search → retrieval → grounded answer
```

**Why chunk?** A PDF can be too large for effective retrieval. Breaking it into coherent, traceable passages lets us retrieve only the relevant evidence, preserve section context, and cite the source pages.

## 2. Core concepts — in plain English

| Concept                  | Meaning                                                                                   | Why we need it                                   |
| ------------------------ | ----------------------------------------------------------------------------------------- | ------------------------------------------------ |
| Markdown extraction      | Document Intelligence returns structured text rather than only raw PDF bytes              | Keeps headings and tables recognisable           |
| Structure-aware chunking | Split by meaningful sections/paragraphs/tables rather than arbitrary character positions  | Better retrieval context                         |
| Chunk-size budget        | Upper bound on each chunk's content length, configured at 1,500 characters in our PoC     | Predictable size and downstream embedding inputs |
| Heading context          | Prefix a chunk with its relevant heading hierarchy                                        | A split passage remains understandable           |
| Source span              | `(Offset, Length)` identifying characters in the **original extracted Markdown**          | Links transformed chunk text back to its source  |
| Page span                | A source span associated with a numbered PDF page                                         | Enables page-level citations                     |
| Deterministic chunk ID   | Stable SHA-256-derived identifier for the same document version, strategy and chunk index | Repeatable indexing and updates                  |
| Source version           | SHA-256 of original PDF bytes                                                             | Detect document changes                          |
| Strategy version         | Identifies chunking algorithm/settings, e.g. `structure-v3-max1500`                       | Re-index if the chunking approach changes        |

**Critical distinction:** A source span is **not** an offset into the final chunk. It points into `ExtractedDocument.Markdown`, which must remain the canonical source text for citation mapping.

## 3. Domain and application models

The important shapes in our implementation are:

```csharp
public sealed record ContentSpan(int Offset, int Length);

public sealed record ExtractedPage(
    int PageNumber,
    IReadOnlyList<ContentSpan> Spans);

public sealed record ExtractedDocument(
    string DocumentId,
    string BlobPath,
    string PolicyType,
    string Markdown,
    IReadOnlyList<ExtractedPage> Pages,
    string SourceVersion = "");

public sealed record ContentPiece(
    string Text,
    IReadOnlyList<ContentSpan> SourceSpans);

public sealed record DocumentChunk(
    string ChunkId,
    string DocumentId,
    string PolicyType,
    string BlobPath,
    string SectionTitle,
    string Content,
    IReadOnlyList<int> PageNumbers,
    int ChunkIndex,
    string SourceVersion,
    string ChunkingStrategyVersion);
```

`ParseBlocks()` also uses an internal `MarkdownBlock(Text, IsHeading, SourceSpan)` representation.

**Separation of responsibilities:**

- **Infrastructure:** Azure Blob and Document Intelligence integration.
- **Application:** chunking policy, splitting, source-span resolution and models/contracts.
- **API:** development endpoints and dependency injection.
- **Tests:** in-memory, deterministic unit tests that do not require Azure calls.

## 4. Step A — Extract PDF as Markdown

The extraction component uses Document Intelligence's `prebuilt-layout` model with Markdown output:

```csharp
var options = new AnalyzeDocumentOptions(
    "prebuilt-layout",
    BinaryData.FromBytes(documentBytes))
{
    OutputContentFormat = DocumentContentFormat.Markdown
};
```

It returns one Markdown string plus `ExtractedPage` records containing service-provided page spans. Our source version is calculated from the original PDF bytes, not the extracted text.

**SDK note:** We encountered build errors trying to set `AnalyzeDocumentOptions.StringIndexType` / `StringIndexType.Utf16CodeUnit`. Those members were not available publicly in the installed SDK. We removed that assignment. **We have not yet verified the service's index convention against .NET UTF-16 for all Unicode text.** This is an important outstanding citation check.

## 5. Step B — Parse Markdown into structural blocks

`StructureAwareDocumentChunker.ParseBlocks()` identifies:

1. **Headings** such as `# Home Policy Schedule`, `## Your Excesses`, `### Buildings`.
2. **Paragraphs**, collecting text until a structural boundary.
3. **HTML tables**, preserving the table as a distinct block rather than mixing its rows with surrounding prose.

**The parser does not enforce the 1,500-character limit.** It answers: _What is this content, and where was it located in the original Markdown?_

Example:

```markdown
# Home Policy Schedule

This schedule explains your cover.

## Buildings

<table>
<tr><td>Excess</td><td>£100</td></tr>
</table>
```

Conceptual output:

```text
Heading:   # Home Policy Schedule
Paragraph: This schedule explains your cover.
Heading:   ## Buildings
Table:     <table>...</table>
```

Each block retains an original source span. A new heading flushes any pending paragraph; a table is treated as a separate structural unit.

### Whitespace and offset alignment

If the original Markdown contains leading spaces, `Trim()` changes the visible text. **The source offset must be adjusted by the number of leading characters removed**; otherwise it points to the wrong place.

```csharp
var rawText = paragraph.ToString();
var leadingWhitespace = rawText.Length - rawText.TrimStart().Length;
var trimmedText = rawText.Trim();

var span = new ContentSpan(
    paragraphStart + leadingWhitespace,
    trimmedText.Length);
```

The same principle applies to table blocks and any other trimmed source text. **Confirm this adjustment exists in your current `ParseBlocks()` implementation**; we identified it as an important correction, but did not independently verify the final repository state.

## 6. Step C — Split oversized content while retaining offsets

`IContentBlockSplitter.Split(content, sourceOffset, maxCharacters)` returns `IReadOnlyList<ContentPiece>`.

Rules in our current PoC:

- **Short paragraph/table:** return a single `ContentPiece` and a span covering its original content.
- **Long paragraph:** split into size-bounded pieces, preserving original word positions.
- **Oversized HTML table:** throw `NotSupportedException` until safe row-level splitting and offset tracking are implemented.

The long-paragraph splitter uses `Regex.Matches(content, @"\S+")`. Each word's source span is calculated using:

```csharp
new ContentSpan(sourceOffset + word.Index, word.Length)
```

### Worked whitespace example

```text
Original Markdown:  "Windscreen    repair   is covered."
Final piece text:   "Windscreen repair is covered."
```

The piece's display text is normalised, but its **individual word spans** still point to their original positions. This is why `ContentPiece` holds a **list** of spans instead of assuming one continuous range.

For extremely long individual tokens, the splitter can subdivide the token into fixed-size portions.

**Why not simply take the final chunk's character index?** Because inserted headings, trimmed whitespace and normalised spacing mean chunk text positions no longer correspond to the source Markdown.

## 7. Step D — Pack pieces into final chunks

`StructureAwareDocumentChunker.Chunk(document)`:

1. Calls `ParseBlocks(document.Markdown)`.
2. Maintains a heading hierarchy using heading levels (`#`, `##`, `###`).
3. On a new heading, saves any current body-containing chunk and updates the hierarchy.
4. Calculates the available size after reserving space for heading context and separators.
5. Calls `ContentBlockSplitter.Split(...)` for body blocks.
6. Greedily appends pieces while the resulting chunk stays within the configured size.
7. Tracks each appended piece's `SourceSpans` in `currentSourceSpans`.
8. Saves the chunk with page numbers derived from those spans.

Key ideas:

```csharp
var availableCharacters =
    _options.MaxCharacters - headingContext.Length - 2;

var pieces = _splitter.Split(
    block.Text,
    block.SourceSpan.Offset,
    availableCharacters);

// After appending a piece to currentContent:
currentSourceSpans.AddRange(piece.SourceSpans);
```

**Why repeat headings?** A paragraph about a £100 excess is ambiguous by itself. Prefixing `# Premium Details` / `## Your Excesses` / `### Buildings` tells the future search index and LLM what the amount refers to.

**Why save at heading boundaries?** We prefer coherent sections over packing unrelated topics into the same chunk. Thus a page boundary does not necessarily imply a chunk boundary, and a heading boundary can prevent a chunk from spanning pages.

### Final chunk trimming and clearing

When saving a chunk:

```csharp
var pageNumbers = _pageNumberResolver.Resolve(
    currentSourceSpans,
    document.Pages);

// Build DocumentChunk using currentContent.ToString().Trim()
// and pageNumbers, then reset temporary state:
currentContent.Clear();
currentSourceSpans.Clear();
```

- `Trim()` removes outer whitespace **from the final chunk text only**.
- The original extracted Markdown and its spans are unchanged.
- Page numbers are calculated **before** the span buffer is cleared.
- Clearing the buffers prepares the next chunk; it does not erase the already-created chunk.

## 8. Step E — Resolve original spans to PDF page numbers

`PageNumberResolver` compares each piece/chunk source span with each page's source spans and returns **distinct, sorted page numbers**.

Use **half-open intervals**: `[Offset, Offset + Length)`.

For two spans A and B, they overlap when:

```csharp
A.Offset < B.Offset + B.Length &&
B.Offset < A.Offset + A.Length
```

The strict `<` is important: merely touching at a boundary is **not** overlap.

### Worked example from our real two-page PDF

Our diagnostic output for the home policy PDF was:

```json
{
  "markdownLength": 4886,
  "pages": [
    { "pageNumber": 1, "spans": [{ "offset": 0, "length": 2403 }] },
    { "pageNumber": 2, "spans": [{ "offset": 2403, "length": 2483 }] }
  ]
}
```

Therefore:

```text
Page 1: [0, 2403)
Page 2: [2403, 4886)
```

| Chunk source span | Page(s)  | Reason                         |
| ----------------- | -------- | ------------------------------ |
| `[100, 300)`      | `[1]`    | Entirely on page 1             |
| `[2300, 2403)`    | `[1]`    | Ends exactly at page 2's start |
| `[2403, 2503)`    | `[2]`    | Starts exactly at page 2       |
| `[2300, 2500)`    | `[1, 2]` | Crosses boundary at 2403       |

Our real diagnostic returned eight chunks, with chunks 0–3 on page 1 and chunks 4–7 on page 2. The sections looked sensible (Home Policy Schedule and Premium Details on page 1; Your Excesses, Buildings, Contents, Cover and Limits on page 2). **This is encouraging but not conclusive proof of Unicode offset alignment.**

### Why source spans rather than repeated headings?

Our current citation calculation uses body-content source spans. Repeated heading prefixes are contextual text generated by the chunker; citing a page solely because that heading originally appeared there could mislead readers about where the supporting paragraph is located.

## 9. Stable IDs, source versions and re-indexing

We implemented:

- **`DocumentVersionCalculator`**: SHA-256 hash of original PDF bytes (`SourceVersion`).
- **`ChunkIdGenerator`**: deterministic SHA-256-based ID using document identity, source version, chunking strategy version and chunk index.
- **`ChunkingOptions`**: e.g. `MaxCharacters = 1500`, `StrategyVersion = "structure-v3-max1500"`.

Why this matters:

```text
Same PDF + same strategy → same chunk IDs
Changed PDF              → changed source version → new chunk IDs
Changed chunking strategy → new strategy version → new chunk IDs
```

**Operational note for later indexing:** Stable IDs help with upserts, but when a PDF or strategy changes, we must also delete obsolete chunks from the search index. Deterministic IDs alone do not perform cleanup.

## 10. Tests completed

We added and ran tests covering the core behaviours. Examples:

| Area                 | Test / assertion                                                           |
| -------------------- | -------------------------------------------------------------------------- |
| Chunk IDs            | Same document/version/strategy yields same IDs                             |
| Versioning           | New source version changes IDs                                             |
| Heading hierarchy    | Correct parent/child headings are preserved                                |
| Long sections        | Text splits and heading context remains available                          |
| Paragraph + table    | Supported-size table and paragraph are retained                            |
| Splitter             | Short paragraph returns one piece                                          |
| Splitter             | Long paragraph returns multiple pieces                                     |
| Splitter             | Original word offsets are preserved                                        |
| Splitter             | Oversized table currently throws explicitly                                |
| Page resolver        | Single-page overlap returns that page                                      |
| Page resolver        | Cross-page overlap returns both pages                                      |
| Boundary behaviour   | A span ending exactly at a page boundary does not cite the next page       |
| End-to-end in-memory | Chunker returns correct pages for a paragraph spanning two synthetic pages |

Run from the repository root (adjust paths to your checkout):

```bash
dotnet build backend/PolicyWise.Api
dotnet test backend/PolicyWise.Application.Tests
```

**What the tests prove:** Our _in-memory_ block → piece → chunk → page mapping works for the tested inputs. They do not, by themselves, prove that every real Document Intelligence response uses the same character indexing convention.

## 11. Real-document diagnostic checklist

For a development-only chunk diagnostic endpoint, inspect:

- `DocumentId` and `MarkdownLength`.
- Each page's `(PageNumber, Offset, Length)`.
- Each chunk's `ChunkIndex`, `SectionTitle`, `PageNumbers`, and a `ContentPreview`.
- Whether the returned page spans fit within the Markdown string.
- Whether chunk citations agree with the actual PDF pages.

Example diagnostic shape:

```json
{
  "documentId": "car/car/House Policy Schedule - Home Policy.pdf",
  "markdownLength": 4886,
  "pages": [
    { "pageNumber": 1, "spans": [{ "offset": 0, "length": 2403 }] },
    { "pageNumber": 2, "spans": [{ "offset": 2403, "length": 2483 }] }
  ],
  "chunks": [
    { "chunkIndex": 0, "sectionTitle": "Introduction", "pageNumbers": [1] },
    {
      "chunkIndex": 4,
      "sectionTitle": "Premium Details: (Debit Card) → Your Excesses:",
      "pageNumbers": [2]
    }
  ]
}
```

**Security note:** Development endpoints can expose insurance policy content, personal data and financial details. Keep these diagnostics development-only and avoid logging complete documents in production.

## 12. Design decisions and trade-offs

**Why structure-aware rather than fixed 1,500-character slicing?** It avoids arbitrary cuts through headings and tables and retains meaningful section context.

**Why keep both final text and source spans?** Chunk text is optimised for retrieval; spans are optimised for traceability. They solve different problems.

**Why use a separate `IPageNumberResolver`?** Its interval-overlap logic is independently testable and reusable.

**Why split paragraphs into word-based pieces?** It preserves original positions while respecting the size budget. The current algorithm is size-aware, not fully sentence-aware; semantic sentence boundaries could be an enhancement.

**Why reject oversized tables for now?** Naively splitting HTML may break rows, headers and citation positions. An explicit failure is safer than silently generating misleading evidence.

**Why not assume each chunk belongs to one page?** A paragraph can physically cross a page boundary, so `PageNumbers` is a list.

## 13. Remaining work (parked)

These are **not completed**, and should be revisited before production use:

1. **Oversized HTML tables:** split by rows while preserving useful headers, valid HTML/context and original source spans. Handle complex merged cells deliberately.
2. **Unicode indexing verification:** establish whether Document Intelligence page offsets and C# Markdown offsets use the same indexing convention (especially supplementary Unicode characters such as emoji); configure or convert offsets if needed.
3. **Whitespace-offset audit:** confirm leading-trim offset adjustment is applied consistently in `ParseBlocks()` for paragraphs and tables.
4. **Regression/edge cases:** empty or heading-only Markdown; oversized heading context; long indivisible words; mixed-page chunks; strict maximum-size checks; repeated table headers and citation integrity.
5. **Production safety:** define behaviour for extraction failures, unsupported tables, invalid spans, version changes, indexing retries, and sensitive-data logging.
6. **Optional future metadata:** a `ChunkContentType` (Text/Table/Mixed) could help retrieval and rendering; not necessary for the next learning milestone.

## 14. How this feeds Milestone 4 — embeddings and vector search

Our next milestone consumes the **existing** `DocumentChunk` output:

```text
INDEXING (when PDF is added/updated)
PDF → Extract → Chunk → Embed chunk.Content → Azure AI Search
                              │
                              └─ Store metadata:
                                 ChunkId, DocumentId, SourceVersion,
                                 SectionTitle, PageNumbers, BlobPath,
                                 PolicyType, ChunkingStrategyVersion

QUERYING (for each broker question)
Question → Embed question → Vector / hybrid search → Relevant chunks
         → Grounded answer with document/page citations
```

**Important:** We do **not** re-embed every document for every user question. We embed documents during indexing and embed the question at query time. Both sides must use compatible embedding models and dimensions.

## 15. Five-minute revision questions

1. **Does `ParseBlocks()` enforce the chunk-size limit?** No. It identifies structure and source locations; the splitter and chunker enforce size constraints.
2. **Why is `ContentPiece.SourceSpans` a list?** A piece can be rebuilt from multiple non-contiguous original words after whitespace normalisation.
3. **Why can a chunk cite `[1, 2]`?** Its source text overlaps both pages.
4. **Does `Trim()` invalidate the original spans?** Not if the parser calculates source offsets correctly and spans continue to reference the untouched original Markdown.
5. **Why repeat heading context?** It keeps a split passage meaningful outside the full document.
6. **Why use half-open intervals?** Exact boundary contact should not count as overlap.
7. **What causes chunk IDs to change?** A changed source version, strategy version, document identity or chunk index.
8. **What's unfinished?** Oversized-table handling, full Unicode offset verification and final hardening tests.
9. **What's next?** Generate embeddings from chunks, index vectors plus metadata, then retrieve relevant passages for questions.

---

**Milestone 3 checkpoint:** Core chunking and page citation logic implemented; unit tests passed; a real two-page policy produced plausible page citations. Remaining hardening is intentionally parked while learning embeddings and Azure AI Search.
