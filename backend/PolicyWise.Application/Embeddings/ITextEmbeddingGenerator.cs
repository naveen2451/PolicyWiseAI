namespace PolicyWise.Application.Embeddings;

public interface ITextEmbeddingGenerator
{
    Task<ReadOnlyMemory<float>> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default
    );
}
