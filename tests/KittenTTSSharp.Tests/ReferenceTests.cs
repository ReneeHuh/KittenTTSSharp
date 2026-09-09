using System.Text.Json;

namespace KittenTTSSharp.Tests;

public sealed class ReferenceTests
{
    public static IEnumerable<object[]> Cases(string category)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "python-reference.json")));
        return document.RootElement.GetProperty(category).EnumerateArray()
            .Select(e => new object[] { e.GetProperty("input").GetString()!, e.GetProperty("expected").Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Cases), "normalization")]
    public void NormalizationMatchesPython(string input, JsonElement expected) => Assert.Equal(expected.GetString(), TextNormalizer.Normalize(input));

    [Theory]
    [MemberData(nameof(Cases), "preprocessing")]
    public void SynthesisPreprocessingMatchesPython(string input, JsonElement expected) => Assert.Equal(expected.GetString(), TextPreprocessor.Process(input));

    [Theory]
    [MemberData(nameof(Cases), "chunking")]
    public void ChunkingMatchesPython(string input, JsonElement expected) => Assert.Equal(expected.Deserialize<string[]>(), TextChunker.Chunk(input));

    [Theory]
    [MemberData(nameof(Cases), "tokenization")]
    public void VocabularyAndUnicodeTokenizationMatchPython(string input, JsonElement expected) => Assert.Equal(expected.Deserialize<long[]>(), PhonemeTokenizer.Encode(input));

    [Fact]
    public void NormalizationSpansMapOriginalAndReplacement()
    {
        var result = TextNormalizer.NormalizeWithSpans("Fig. 2");
        Assert.Equal("Figure two", result.Text);
        Assert.Equal(new[] { new NormalizedSpan(0, 4, 0, 6, "abbreviation"), new NormalizedSpan(5, 6, 7, 10, "number") }, result.Spans);
    }

    [Fact]
    public void SpansRemainValidWithUnicodeAndTrimmedWhitespace()
    {
        const string input = "  Cafe\u0301 Fig. 2   ";
        var result = TextNormalizer.NormalizeWithSpans(input);
        var abbreviation = Assert.Single(result.Spans, s => s.Reason == "abbreviation");
        Assert.Equal("Fig.", input[abbreviation.OriginalStartChar..abbreviation.OriginalEndChar]);
        Assert.Equal("Figure", result.Text[abbreviation.NormalizedStartChar..abbreviation.NormalizedEndChar]);
        Assert.All(result.Spans, s => Assert.InRange(s.NormalizedEndChar, s.NormalizedStartChar, result.Text.Length));
    }

    [Fact]
    public void PunctuationWithoutNumbersDoesNotThrow() => Assert.Equal("Hello, world.", TextNormalizer.Normalize("Hello, world."));

    [Fact]
    public void UnsupportedLocaleFailsExplicitly() => Assert.Throws<NotSupportedException>(() => TextNormalizer.Normalize("Bonjour 2026", "fr-FR"));

    [Fact]
    public void LongInputsRemainBoundedAndPreserveText()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 501));
        var chunks = TextChunker.Chunk(text, 31);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 32));
        Assert.Equal(text, string.Concat(chunks.Select(c => c.TrimEnd(','))));
        Assert.All(chunks, chunk => Assert.False(char.IsHighSurrogate(chunk[^2])));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Chunk("hello", 0));
    }
}
