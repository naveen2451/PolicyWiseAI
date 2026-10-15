namespace PolicyWise.Application.Evaluation;

public sealed record RagEvaluationInput(string Question, string Context, string Answer);

public sealed record RagEvaluationSubmission(string RunId, string Status);

public sealed record RagEvaluationRunResult(
    string RunId,
    string Status,
    string RawRunJson,
    string? RawOutputItemsJson
);

public interface IRagEvaluationService
{
    Task<RagEvaluationSubmission> SubmitAsync(
        RagEvaluationInput input,
        CancellationToken cancellationToken = default
    );

    Task<RagEvaluationRunResult> GetResultAsync(
        string runId,
        CancellationToken cancellationToken = default
    );
}
