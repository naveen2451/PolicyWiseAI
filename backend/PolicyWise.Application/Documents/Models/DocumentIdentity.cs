namespace PolicyWise.Application.Documents.Models;

public sealed record DocumentIdentity(
    string DocumentId,
    string SourceVersion,
    string ChunkingStrategyVersion
);

// The identity fields have different responsibilities.
// Field	Purpose
// DocumentId	Identifies the logical insurance document
// SourceVersion	Identifies the exact PDF contents
// ChunkingStrategyVersion	Identifies the chunking algorithm and configuration