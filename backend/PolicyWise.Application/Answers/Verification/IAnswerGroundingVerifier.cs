using PolicyWise.Application.Search.Models;

namespace PolicyWise.Application.Answers.Verification;

public interface IAnswerGroundingVerifier
{
    Task<GroundingVerificationResult> VerifyAsync(
        string question,
        string generatedAnswer,
        IReadOnlyList<RetrievedDocumentChunk> citedChunks,
        CancellationToken cancellationToken = default
    );
}
