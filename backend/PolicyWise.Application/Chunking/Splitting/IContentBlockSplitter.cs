using PolicyWise.Application.Chunking.Models;

namespace PolicyWise.Application.Chunking.Splitting;

public interface IContentBlockSplitter
{
    IReadOnlyList<ContentPiece> Split(string content, int sourceOffset, int maxCharacters);
}
