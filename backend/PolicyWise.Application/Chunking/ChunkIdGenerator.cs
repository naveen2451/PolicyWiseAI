using System.Security.Cryptography;
using System.Text;

namespace PolicyWise.Application.Chunking;

public static class ChunkIdGenerator
{
    public static string Generate(
        string documentId,
        string sourceVersion,
        string strategyVersion,
        int chunkIndex)
    {
        var input = string.Join(
            "\n",
            documentId,
            sourceVersion,
            strategyVersion,
            chunkIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(input));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

// Same document + same PDF + same strategy + same index
//                          ↓
//                  Same ChunkId