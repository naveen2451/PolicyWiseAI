namespace PolicyWise.Application.Search.Models;

public sealed record RetrievedDocumentChunk(
    string ChunkId,
    string DocumentId,
    string PolicyType,
    string SectionTitle,
    string Content,
    IReadOnlyList<int> PageNumbers,
    double Score
);
