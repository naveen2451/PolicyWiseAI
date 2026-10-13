namespace PolicyWise.Application.Embeddings;

public static class CosineSimilarity
{
    public static double Calculate(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.IsEmpty)
            throw new ArgumentException("Vectors must have the same non-zero dimensions.");

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        if (magnitudeA == 0 || magnitudeB == 0)
            throw new ArgumentException("Vectors must have non-zero magnitude.");

        return dotProduct / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
