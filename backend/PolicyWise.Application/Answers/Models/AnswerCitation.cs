namespace PolicyWise.Application.Answers.Models;

public sealed record AnswerCitation(
    string ChunkId,
    string DocumentId,
    IReadOnlyList<int> PageNumbers
);
