using System.Text.Json.Serialization;

namespace PolicyWise.Infrastructure.Search;

public sealed class PolicyWiseSearchRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = "";

    [JsonPropertyName("documentVersion")]
    public string DocumentVersion { get; set; } = "";

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("blobPath")]
    public string BlobPath { get; set; } = "";

    [JsonPropertyName("policyType")]
    public string PolicyType { get; set; } = "";

    [JsonPropertyName("sectionTitle")]
    public string SectionTitle { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("pageNumbers")]
    public int[] PageNumbers { get; set; } = [];

    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("chunkingStrategyVersion")]
    public string ChunkingStrategyVersion { get; set; } = "";

    [JsonPropertyName("contentVector")]
    public float[] ContentVector { get; set; } = [];
}
