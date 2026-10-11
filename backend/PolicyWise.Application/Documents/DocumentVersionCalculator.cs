using System.Security.Cryptography;

namespace PolicyWise.Application.Documents;

public static class DocumentVersionCalculator
{
    public static async Task<string> CalculateAsync(
        Stream documentStream,
        CancellationToken cancellationToken = default)
    {
        if (!documentStream.CanSeek)
        {
            throw new ArgumentException(
                "A seekable stream is required.",
                nameof(documentStream));
        }

        documentStream.Position = 0;

        var hash = await SHA256.HashDataAsync(
            documentStream,
            cancellationToken);

        documentStream.Position = 0;

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}