namespace PolicyWise.Application.Chunking;

public sealed class ChunkingOptions
{
    public int MaxCharacters { get; set; } = 1500;
    public string StrategyVersion { get; set; }
        = "structure-v2-max1500";
}