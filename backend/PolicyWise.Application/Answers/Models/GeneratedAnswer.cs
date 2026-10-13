namespace PolicyWise.Application.Answers.Models;

public sealed record GeneratedAnswer(string Answer, IReadOnlyList<string> CitedChunkIds);
