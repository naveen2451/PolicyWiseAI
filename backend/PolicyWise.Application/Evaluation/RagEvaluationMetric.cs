namespace PolicyWise.Application.Evaluation;

public sealed record RagEvaluationMetric(
    string Name,
    double? Score,
    double? Threshold,
    bool? Passed,
    string Status,
    string? Reason
);

public sealed record EvaluationTokenUsage(
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    int CachedTokens
);

public sealed record RagEvaluationMetrics(
    string RunId,
    string Status,
    IReadOnlyList<RagEvaluationMetric> Metrics,
    EvaluationTokenUsage? TokenUsage
);
