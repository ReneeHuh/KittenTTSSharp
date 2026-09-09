using System.Text.Json;

namespace KittenTTSSharp.Tests;

// These checks use real native libraries/model weights; ordinary dotnet test stays offline.
public sealed class NativeTheoryAttribute : TheoryAttribute
{
    public NativeTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("KITTENTTS_TEST_NATIVE") != "1")
            Skip = "Set KITTENTTS_TEST_NATIVE=1 and configure eSpeak NG to run native pronunciation tests.";
    }
}

public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KITTENTTS_TEST_MODEL")))
            Skip = "Set KITTENTTS_TEST_MODEL to a downloaded model directory to run real inference tests.";
    }
}

public sealed class NativeIntegrationTests
{
    public static IEnumerable<object[]> PhonemeCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "native-reference.json")));
        return document.RootElement.GetProperty("cases").EnumerateArray()
            .Select(e => new object[] { e.GetProperty("input").GetString()!, e.GetProperty("tokens").Deserialize<long[]>()! }).ToArray();
    }

    [NativeTheory]
    [MemberData(nameof(PhonemeCases))]
    public void NativePhonemeTokensMatchPython(string input, long[] expected)
    {
        var phonemizer = new EspeakPhonemizer();
        Assert.Equal(expected, PhonemeTokenizer.Encode(phonemizer.Phonemize(input)));
    }

    [ModelFact]
    public async Task RealModelSupportsVoicesSpeedStreamingAndValidation()
    {
        using var model = await KittenTts.LoadAsync(new KittenTtsOptions
        {
            ModelDirectory = Environment.GetEnvironmentVariable("KITTENTTS_TEST_MODEL"), IntraOpNumThreads = 2
        });
        Assert.Equal(8, model.VoiceIds.Count);
        Assert.Equal(8, model.AvailableVoices.Count);
        foreach (var voice in model.AvailableVoices)
        {
            var samples = model.Generate("Hello, world.", voice);
            Assert.InRange(samples.Length, KittenTts.SampleRate / 2, KittenTts.SampleRate * 15);
            Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
            Assert.True(samples.Any(s => Math.Abs(s) > 0.01f), $"Voice {voice} generated silence.");
        }
        var normal = model.Generate("This is a test of the speech speed.", speed: 1);
        var faster = model.Generate("This is a test of the speech speed.", speed: 1.5f);
        Assert.True(faster.Length < normal.Length);
        var chunks = model.GenerateStream("First sentence. Second sentence.").ToArray();
        Assert.Equal(2, chunks.Length);
        Assert.All(chunks, c => Assert.NotEmpty(c));
        Assert.Empty(model.Generate("  "));
        Assert.Throws<ArgumentException>(() => model.Generate("hello", "missing-voice"));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Generate("hello", speed: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Generate("hello", speed: float.NaN));
        Assert.Throws<ArgumentException>(() => model.Generate("hello\0world"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => model.Generate("hello", cancellationToken: cancellation.Token));
        model.Dispose();
        Assert.Throws<ObjectDisposedException>(() => model.Generate("hello"));
    }
}
