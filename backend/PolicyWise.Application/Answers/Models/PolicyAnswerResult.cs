using PolicyWise.Application.Evidence;

namespace PolicyWise.Application.Answers.Models;

public sealed record PolicyAnswerResult(
    string Answer,
    EvidenceStatus EvidenceStatus,
    IReadOnlyList<AnswerCitation> Citations
);
