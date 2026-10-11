namespace PolicyWise.Application.Documents;

public sealed record BlobDocumentInfo(
    string BlobPath,
    string FileName,
    string PolicyType,
    long SizeBytes,
    DateTimeOffset? LastModified
);