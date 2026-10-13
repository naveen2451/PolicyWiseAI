using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Embeddings;
using PolicyWise.Application.Embeddings;

namespace PolicyWise.Infrastructure.Embeddings;

public sealed class AzureOpenAIEmbeddingGenerator : ITextEmbeddingGenerator
{
    private readonly EmbeddingClient _embeddingClient;

    public AzureOpenAIEmbeddingGenerator(string endpoint, string deploymentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName);

        var azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential());

        _embeddingClient = azureOpenAIClient.GetEmbeddingClient(deploymentName);
    }

    public async Task<ReadOnlyMemory<float>> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var response = await _embeddingClient.GenerateEmbeddingAsync(
            text,
            cancellationToken: cancellationToken
        );

        return response.Value.ToFloats();
    }
}
