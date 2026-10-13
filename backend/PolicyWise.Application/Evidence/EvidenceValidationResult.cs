namespace PolicyWise.Application.Evidence;

public sealed record EvidenceValidationResult(
    EvidenceStatus Status,
    IReadOnlyList<string> SupportingChunkIds,
    string Reason
);
