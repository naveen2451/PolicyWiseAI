namespace PolicyWise.Application.Chunking.Models;

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
    string ChunkingStrategyVersion
);