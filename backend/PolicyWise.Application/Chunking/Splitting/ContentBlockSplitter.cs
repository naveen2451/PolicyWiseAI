using System.Text;
using System.Text.RegularExpressions;
using PolicyWise.Application.Chunking.Models;
using PolicyWise.Application.Extraction.Models;

namespace PolicyWise.Application.Chunking.Splitting;

public sealed class ContentBlockSplitter : IContentBlockSplitter
{
    public IReadOnlyList<ContentPiece> Split(string content, int sourceOffset, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (maxCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));

        if (sourceOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOffset));

        if (string.IsNullOrWhiteSpace(content))
            return [];

        // A complete block that already fits.
        if (content.Length <= maxCharacters)
        {
            return [new ContentPiece(content, [new ContentSpan(sourceOffset, content.Length)])];
        }

        // Tables require row-level mapping.
        // We will implement that in the next step.
        if (content.TrimStart().StartsWith("<table", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Offset-aware splitting of oversized tables is not yet implemented."
            );
        }

        return SplitParagraphWithOffsets(content, sourceOffset, maxCharacters);
    }

    private static IReadOnlyList<ContentPiece> SplitParagraphWithOffsets(
        string content,
        int sourceOffset,
        int maxCharacters
    )
    {
        var results = new List<ContentPiece>();

        // Match non-whitespace sequences and retain their original positions.
        var words = Regex.Matches(content, @"\S+");

        var currentText = new StringBuilder();
        var currentSpans = new List<ContentSpan>();

        void Flush()
        {
            if (currentText.Length == 0)
                return;

            results.Add(new ContentPiece(currentText.ToString(), currentSpans.ToArray()));

            currentText.Clear();
            currentSpans.Clear();
        }

        foreach (Match word in words)
        {
            if (word.Length > maxCharacters)
            {
                Flush();

                // An unusually long token must also be split.
                for (var index = 0; index < word.Length; index += maxCharacters)
                {
                    var length = Math.Min(maxCharacters, word.Length - index);

                    results.Add(
                        new ContentPiece(
                            word.Value.Substring(index, length),
                            [new ContentSpan(sourceOffset + word.Index + index, length)]
                        )
                    );
                }

                continue;
            }

            var requiredLength =
                currentText.Length == 0 ? word.Length : currentText.Length + 1 + word.Length;

            if (requiredLength > maxCharacters)
                Flush();

            if (currentText.Length > 0)
                currentText.Append(' ');

            currentText.Append(word.Value);

            currentSpans.Add(new ContentSpan(sourceOffset + word.Index, word.Length));
        }

        Flush();

        return results;
    }

    // 1. Split paragraphs at sentence boundaries where possible.
    private static IReadOnlyList<string> SplitParagraph(string content, int maxCharacters)
    {
        var sentences = Regex.Split(content, @"(?<=[.!?])\s+");
        var pieces = new List<string>();

        foreach (var sentence in sentences)
        {
            if (sentence.Length <= maxCharacters)
            {
                pieces.Add(sentence);
                continue;
            }

            pieces.AddRange(SplitLongSentence(sentence, maxCharacters));
        }

        return PackPieces(pieces, maxCharacters, " ");
    }

    // 2. Split long sentences at word boundaries.
    private static IReadOnlyList<string> SplitLongSentence(string sentence, int maxCharacters)
    {
        var words = Regex.Split(sentence.Trim(), @"\s+");
        return PackPieces(words, maxCharacters, " ");
    }

    // 3. Group smaller pieces without exceeding the limit.
    private static IReadOnlyList<string> PackPieces(
        IEnumerable<string> pieces,
        int maxCharacters,
        string separator
    )
    {
        var result = new List<string>();
        var buffer = new StringBuilder();

        foreach (var piece in pieces)
        {
            if (string.IsNullOrWhiteSpace(piece))
                continue;

            var separatorLength = buffer.Length > 0 ? separator.Length : 0;

            if (buffer.Length > 0 && buffer.Length + separatorLength + piece.Length > maxCharacters)
            {
                result.Add(buffer.ToString());
                buffer.Clear();
            }

            if (buffer.Length > 0)
                buffer.Append(separator);

            buffer.Append(piece);
        }

        if (buffer.Length > 0)
            result.Add(buffer.ToString());

        return result;
    }

    // 4. Split HTML tables by rows, preserving their header.
    private static IReadOnlyList<string> SplitTable(string html, int maxCharacters)
    {
        var rows = Regex
            .Matches(html, @"<tr\b[^>]*>[\s\S]*?</tr>", RegexOptions.IgnoreCase)
            .Select(match => match.Value)
            .ToList();

        if (rows.Count == 0)
            return [html];

        var openingTag = Regex.Match(html, @"<table\b[^>]*>", RegexOptions.IgnoreCase).Value;

        if (string.IsNullOrEmpty(openingTag))
            return [html];

        // Supports simple tables where the first row is the header.
        var header = rows[0];
        var dataRows = rows.Skip(1).ToList();

        if (dataRows.Count == 0)
            return [html];

        var results = new List<string>();
        var currentRows = new List<string>();

        string BuildTable(IEnumerable<string> selectedRows)
        {
            var builder = new StringBuilder();

            builder.AppendLine(openingTag);
            builder.AppendLine(header);

            foreach (var row in selectedRows)
                builder.AppendLine(row);

            builder.Append("</table>");

            return builder.ToString();
        }

        foreach (var row in dataRows)
        {
            var candidateRows = currentRows.Append(row).ToList();
            var candidateTable = BuildTable(candidateRows);

            if (currentRows.Count > 0 && candidateTable.Length > maxCharacters)
            {
                results.Add(BuildTable(currentRows));
                currentRows.Clear();
            }

            currentRows.Add(row);
        }

        if (currentRows.Count > 0)
            results.Add(BuildTable(currentRows));

        return results;
    }
}
