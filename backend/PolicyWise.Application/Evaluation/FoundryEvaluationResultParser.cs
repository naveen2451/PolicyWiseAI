using System.Text.Json;
using PolicyWise.Application.Evaluation;

namespace PolicyWise.Infrastructure.Evaluation;

public static class FoundryEvaluationResultParser
{
    private static readonly HashSet<string> DashboardMetrics = new(StringComparer.OrdinalIgnoreCase)
    {
        "Groundedness",
        "Retrieval",
        "Relevance",
        "ResponseCompleteness",
    };

    public static RagEvaluationMetrics Parse(RagEvaluationRunResult result)
    {
        var metrics = new List<RagEvaluationMetric>();
        EvaluationTokenUsage? usage = null;

        if (!string.IsNullOrWhiteSpace(result.RawOutputItemsJson))
        {
            using var output = JsonDocument.Parse(result.RawOutputItemsJson);

            foreach (var item in output.RootElement.GetProperty("data").EnumerateArray())
            {
                foreach (var metric in item.GetProperty("results").EnumerateArray())
                {
                    var name = metric.GetProperty("name").GetString()!;

                    // Filter by evaluator name, not metric key:
                    // OutputQuality also emits a "groundedness" metric.
                    if (!DashboardMetrics.Contains(name))
                        continue;

                    metrics.Add(
                        new RagEvaluationMetric(
                            Name: name,
                            Score: ReadDouble(metric, "score"),
                            Threshold: ReadDouble(metric, "threshold"),
                            Passed: ReadBool(metric, "passed"),
                            Status: ReadString(metric, "status") ?? "unknown",
                            Reason: ReadString(metric, "reason")
                        )
                    );
                }
            }
        }

        using (var run = JsonDocument.Parse(result.RawRunJson))
        {
            if (run.RootElement.TryGetProperty("per_model_usage", out var models))
            {
                // Use the actual GPT model entry.
                // Do not double-count the azure_ai_evaluation aggregate.
                foreach (var model in models.EnumerateArray())
                {
                    var modelName = ReadString(model, "model_name");

                    if (
                        modelName is null
                        || modelName.Equals(
                            "azure_ai_evaluation",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        continue;

                    usage = new EvaluationTokenUsage(
                        InputTokens: ReadInt(model, "prompt_tokens"),
                        OutputTokens: ReadInt(model, "completion_tokens"),
                        TotalTokens: ReadInt(model, "total_tokens"),
                        CachedTokens: ReadInt(model, "cached_tokens")
                    );

                    break;
                }
            }
        }

        return new RagEvaluationMetrics(result.RunId, result.Status, metrics, usage);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static bool? ReadBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}
