using PolicyWise.Application.Answers.Models;
using PolicyWise.Application.Search.Models;

namespace PolicyWise.Application.Answers;

public interface IAnswerGenerator
{
    Task<GeneratedAnswer> GenerateAsync(
        string question,
        IReadOnlyList<RetrievedDocumentChunk> validatedChunks,
        CancellationToken cancellationToken = default
    );
}
