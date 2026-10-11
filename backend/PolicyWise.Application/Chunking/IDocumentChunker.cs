using PolicyWise.Application.Chunking.Models;
using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Chunking;

public interface IDocumentChunker
{
    IReadOnlyList<DocumentChunk> Chunk(
        ExtractedDocument document);
}

// The chunker interface is synchronous because chunking is local CPU-bound processing. 
// There is no need for async unless we later introduce an external dependency.