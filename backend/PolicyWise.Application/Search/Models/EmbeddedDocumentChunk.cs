using PolicyWise.Application.Chunking.Models;

namespace PolicyWise.Application.Search.Models;

public sealed record EmbeddedDocumentChunk(DocumentChunk Chunk, ReadOnlyMemory<float> Embedding);
