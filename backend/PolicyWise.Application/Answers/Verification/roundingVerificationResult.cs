namespace PolicyWise.Application.Answers.Verification;

public sealed record GroundingVerificationResult(
    GroundingStatus Status,
    IReadOnlyList<string> UnsupportedClaims,
    string Reason
);
