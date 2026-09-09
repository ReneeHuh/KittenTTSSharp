using System.Text.RegularExpressions;

namespace KittenTTSSharp;

public static class TextChunker
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "dr", "prof", "mr", "mrs", "ms", "fig", "figs", "pp", "p", "ch", "sec", "jan", "feb",
        "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec", "al"
    };

    public static IReadOnlyList<string> Chunk(string text, int maxLength = 400)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxLength < 2) throw new ArgumentOutOfRangeException(nameof(maxLength));
        var sentences = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!IsBoundary(text, i)) continue;
            sentences.Add(text[start..(i + 1)]);
            start = i + 1;
        }
        if (start < text.Length) sentences.Add(text[start..]);
        var chunks = new List<string>();
        foreach (var raw in sentences)
        {
            var sentence = raw.Trim();
            if (sentence.Length == 0) continue;
            if (sentence.Length <= maxLength) { chunks.Add(EnsurePunctuation(sentence)); continue; }
            var pending = "";
            foreach (var originalWord in Regex.Split(sentence, @"\s+"))
            {
                var word = originalWord;
                if (pending.Length + word.Length + 1 > maxLength && pending.Length > 0)
                {
                    chunks.Add(EnsurePunctuation(pending));
                    pending = "";
                }
                // Unlike upstream, bound pathological inputs with no whitespace too.
                while (word.Length > maxLength)
                {
                    var take = maxLength;
                    if (char.IsHighSurrogate(word[take - 1])) take--;
                    chunks.Add(EnsurePunctuation(word[..take]));
                    word = word[take..];
                }
                pending = pending.Length == 0 ? word : pending + " " + word;
            }
            if (pending.Length > 0) chunks.Add(EnsurePunctuation(pending));
        }
        return chunks.AsReadOnly();
    }

    private static string EnsurePunctuation(string text) => ".!?,;:".Contains(text[^1]) ? text : text + ",";

    private static bool IsBoundary(string text, int index)
    {
        if (!".!?".Contains(text[index])) return false;
        if (text[index] == '.')
        {
            if (index > 0 && index < text.Length - 1 && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1])) return false;
            var before = text[..index];
            var token = Regex.Match(before, @"([A-Za-z]+)$").Value.ToLowerInvariant();
            if (Abbreviations.Contains(token)) return false;
            if (token is "a" or "p" && index + 1 < text.Length && char.ToLowerInvariant(text[index + 1]) == 'm') return false;
            if (token == "m" && Regex.IsMatch(before, @"\b[ap]\.m$", RegexOptions.IgnoreCase))
            {
                var after = text[(index + 1)..].Trim();
                return after.Length == 0 || char.IsUpper(after[0]);
            }
        }
        return index + 1 == text.Length || char.IsWhiteSpace(text[index + 1]);
    }
}
