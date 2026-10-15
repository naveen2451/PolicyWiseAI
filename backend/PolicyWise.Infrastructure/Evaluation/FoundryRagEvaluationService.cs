using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using PolicyWise.Application.Evaluation;

namespace PolicyWise.Infrastructure.Evaluation;

public sealed class FoundryRagEvaluationService(
    HttpClient httpClient,
    TokenCredential credential,
    IConfiguration configuration
) : IRagEvaluationService
{
    public async Task<RagEvaluationSubmission> SubmitAsync(
        RagEvaluationInput input,
        CancellationToken cancellationToken = default
    )
    {
        var endpoint = configuration["FoundryEvaluation:ProjectEndpoint"]!.TrimEnd('/');

        var evalId = configuration["FoundryEvaluation:EvaluationId"]!;

        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://ai.azure.com/.default"]),
            cancellationToken
        );

        var url = $"{endpoint}/openai/v1/evals/" + $"{Uri.EscapeDataString(evalId)}/runs";

        var payload = new
        {
            name = $"policywise-{Guid.NewGuid():N}",
            data_source = new
            {
                type = "jsonl",
                source = new
                {
                    type = "file_content",
                    content = new[]
                    {
                        new
                        {
                            item = new
                            {
                                query = input.Question,
                                context = input.Context,
                                response = input.Answer,
                            },
                        },
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );

        var root = json.RootElement;

        return new RagEvaluationSubmission(
            root.GetProperty("id").GetString()!,
            root.GetProperty("status").GetString()!
        );
    }

    public async Task<RagEvaluationRunResult> GetResultAsync(
        string runId,
        CancellationToken cancellationToken = default
    )
    {
        var endpoint = configuration["FoundryEvaluation:ProjectEndpoint"]!.TrimEnd('/');

        var evalId = configuration["FoundryEvaluation:EvaluationId"]!;

        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://ai.azure.com/.default"]),
            cancellationToken
        );

        var baseUrl =
            $"{endpoint}/openai/v1/evals/"
            + $"{Uri.EscapeDataString(evalId)}/runs/"
            + Uri.EscapeDataString(runId);

        async Task<string> GetJsonAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var response = await httpClient.SendAsync(request, cancellationToken);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        var runJson = await GetJsonAsync(baseUrl);

        using var document = JsonDocument.Parse(runJson);

        var status = document.RootElement.GetProperty("status").GetString() ?? "unknown";

        string? outputItemsJson = null;

        if (status.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            outputItemsJson = await GetJsonAsync($"{baseUrl}/output_items?limit=100");
        }

        return new RagEvaluationRunResult(runId, status, runJson, outputItemsJson);
    }
}
