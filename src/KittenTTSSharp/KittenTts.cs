using Microsoft.ML.OnnxRuntime;

namespace KittenTTSSharp;

/// <summary>Reusable CPU inference session for KittenTTS ONNX1/ONNX2 models.</summary>
public sealed class KittenTts : IDisposable
{
    public const int SampleRate = 24000;
    public const string DefaultVoice = "expr-voice-5-m";
    private readonly InferenceSession session;
    private readonly IPhonemizer phonemizer;
    private readonly Dictionary<string, VoiceData> voices;
    private readonly ModelConfig config;
    private readonly int maxChunkLength;
    private readonly object gate = new();
    private bool disposed;

    public IReadOnlyList<string> AvailableVoices { get; }
    public IReadOnlyList<string> VoiceIds { get; }
    public string ModelDirectory { get; }

    private KittenTts(ModelFiles files, KittenTtsOptions options, IPhonemizer phonemizer)
    {
        this.phonemizer = phonemizer;
        config = files.Config;
        maxChunkLength = options.MaxChunkLength;
        ModelDirectory = files.Directory;
        voices = VoiceArchive.Load(Path.Combine(files.Directory, config.Voices));
        foreach (var alias in config.VoiceAliases)
            if (!voices.ContainsKey(alias.Value)) throw new InvalidDataException($"Voice alias '{alias.Key}' points to an absent voice.");
        VoiceIds = Array.AsReadOnly(voices.Keys.ToArray());
        AvailableVoices = Array.AsReadOnly(config.VoiceAliases.Count > 0 ? config.VoiceAliases.Keys.ToArray() : voices.Keys.ToArray());
        using var sessionOptions = new SessionOptions { IntraOpNumThreads = options.IntraOpNumThreads };
        session = new InferenceSession(Path.Combine(files.Directory, config.ModelFile), sessionOptions);
        try
        {
            foreach (var name in new[] { "input_ids", "style", "speed" })
                if (!session.InputMetadata.ContainsKey(name)) throw new InvalidDataException($"Model is missing input '{name}'.");
            var style = session.InputMetadata["style"];
            if (style.Dimensions.Length != 2 || voices.Values.Any(v => style.Dimensions[1] > 0 && v.Width != style.Dimensions[1]))
                throw new InvalidDataException("Voice style dimensions do not match the ONNX model.");
        }
        catch { session.Dispose(); throw; }
    }

    public static async Task<KittenTts> LoadAsync(KittenTtsOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new KittenTtsOptions();
        if (options.IntraOpNumThreads < 0 || options.MaxChunkLength < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "Thread count must be nonnegative and chunk length at least two.");
        cancellationToken.ThrowIfCancellationRequested();
        var phonemizer = options.Phonemizer ?? new EspeakPhonemizer(options.EspeakLibraryPath, options.EspeakDataPath);
        var files = await ModelFiles.LoadAsync(options, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new KittenTts(files, options, phonemizer);
    }

    public float[] Generate(string text, string voice = DefaultVoice, float speed = 1, bool cleanText = false,
        CancellationToken cancellationToken = default)
    {
        var chunks = GenerateStream(text, voice, speed, cleanText, cancellationToken).ToArray();
        var total = chunks.Aggregate(0, (sum, chunk) => checked(sum + chunk.Length));
        var audio = new float[total];
        var offset = 0;
        foreach (var chunk in chunks) { chunk.CopyTo(audio, offset); offset += chunk.Length; }
        return audio;
    }

    /// <summary>Yields completed text chunks, each containing mono float samples at 24 kHz.</summary>
    public IEnumerable<float[]> GenerateStream(string text, string voice = DefaultVoice, float speed = 1,
        bool cleanText = false, CancellationToken cancellationToken = default)
    {
        Validate(text, voice, speed);
        cancellationToken.ThrowIfCancellationRequested();
        if (cleanText) text = TextPreprocessor.Process(text);
        foreach (var chunk in TextChunker.Chunk(text, maxChunkLength))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return GenerateSingleChunk(chunk, voice, speed, cancellationToken);
        }
    }

    public float[] GenerateSingleChunk(string text, string voice = DefaultVoice, float speed = 1,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var voiceId = Validate(text, voice, speed);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) return [];
            var ids = PhonemeTokenizer.Encode(phonemizer.Phonemize(text));
            var textLength = text.EnumerateRunes().Count();
            var style = voices[voiceId].GetStyle(textLength);
            var effectiveSpeed = speed * config.SpeedPriors.GetValueOrDefault(voiceId, 1);
            if (!float.IsFinite(effectiveSpeed) || effectiveSpeed <= 0)
                throw new ArgumentOutOfRangeException(nameof(speed), "Speed multiplied by the voice prior must be finite and positive.");
            using var inputIds = OrtValue.CreateTensorValueFromMemory(ids, [1, ids.Length]);
            using var inputStyle = OrtValue.CreateTensorValueFromMemory(style, [1, style.Length]);
            using var inputSpeed = OrtValue.CreateTensorValueFromMemory(new[] { effectiveSpeed }, [1]);
            using var runOptions = new RunOptions();
            using var cancellation = cancellationToken.Register(() => runOptions.Terminate = true);
            try
            {
                using var outputs = session.Run(runOptions, new Dictionary<string, OrtValue>
                {
                    ["input_ids"] = inputIds, ["style"] = inputStyle, ["speed"] = inputSpeed
                }, session.OutputNames);
                cancellationToken.ThrowIfCancellationRequested();
                var data = outputs[0].GetTensorDataAsSpan<float>();
                // Match Python's outputs[0][..., :-5000]. Batch and channel dimensions are singleton.
                return data[..Math.Max(0, data.Length - 5000)].ToArray();
            }
            catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    public void GenerateToFile(string text, string outputPath, string voice = DefaultVoice, float speed = 1,
        bool cleanText = true, CancellationToken cancellationToken = default)
    {
        var audio = Generate(text, voice, speed, cleanText, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        WaveFile.Write(outputPath, audio, SampleRate);
    }

    public static string NormalizeText(string text, string locale = "en-US") => TextNormalizer.Normalize(text, locale);

    private string Validate(string text, string voice, float speed)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(voice);
        if (text.Contains('\0')) throw new ArgumentException("Text must not contain NUL characters.", nameof(text));
        if (!float.IsFinite(speed) || speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed), "Speed must be finite and positive.");
        var id = config.VoiceAliases.GetValueOrDefault(voice, voice);
        if (!voices.ContainsKey(id)) throw new ArgumentException($"Unknown voice '{voice}'. Choose from: {string.Join(", ", AvailableVoices)}.", nameof(voice));
        return id;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            session.Dispose();
        }
    }
}
