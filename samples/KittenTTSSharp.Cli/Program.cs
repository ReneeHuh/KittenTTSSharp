using System.Diagnostics;
using System.Globalization;
using KittenTTSSharp;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("""
        KittenTTSSharp — CPU text-to-speech, no Python runtime

        dotnet run --project samples/KittenTTSSharp.Cli -- --text "Hello, world." --output hello.wav

        --text TEXT           Text to speak (or use --text-file PATH)
        --output PATH         Output WAV file (default: output.wav)
        --voice NAME          Voice alias or ID (default: Jasper)
        --speed NUMBER        Positive speech speed multiplier (default: 1)
        --model REPO          Hugging Face repository (default: KittenML/kitten-tts-mini-0.8)
        --revision REV        Model revision or commit (default: main)
        --cache-dir PATH      Model download cache directory
        --model-dir PATH      Local model directory; disables downloads
        --espeak-library PATH Native eSpeak NG library
        --espeak-data PATH    espeak-ng-data directory or its parent
        --threads NUMBER      ONNX CPU thread count (default: runtime-selected)
        --no-clean            Disable synthesis text preprocessing
        --list-voices         Load model and list voice aliases and IDs
        --normalize          Print read-aloud normalization without loading a model
        --phonemes           Print IPA phonemes without loading a model
        """);
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var flags = new HashSet<string> { "--no-clean", "--list-voices", "--normalize", "--phonemes" };
    var values = new HashSet<string>
    {
        "--text", "--text-file", "--output", "--voice", "--speed", "--model", "--revision", "--cache-dir",
        "--model-dir", "--espeak-library", "--espeak-data", "--threads"
    };
    var parsed = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
    {
        var name = args[i];
        if (!flags.Contains(name) && !values.Contains(name)) throw new ArgumentException($"Unknown option: {name}");
        var value = "true";
        if (values.Contains(name))
        {
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Missing value for {name}.");
            value = args[i];
        }
        if (!parsed.TryAdd(name, value)) throw new ArgumentException($"Duplicate option: {name}");
    }
    if (parsed.ContainsKey("--text") && parsed.ContainsKey("--text-file")) throw new ArgumentException("Choose either --text or --text-file.");
    if (new[] { "--normalize", "--phonemes", "--list-voices" }.Count(parsed.ContainsKey) > 1)
        throw new ArgumentException("Choose one of --normalize, --phonemes, or --list-voices.");
    var text = parsed.GetValueOrDefault("--text");
    if (parsed.TryGetValue("--text-file", out var input)) text = await File.ReadAllTextAsync(input, cancellation.Token);
    if (!parsed.ContainsKey("--list-voices") && string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Supply nonempty --text or --text-file.");
    if (parsed.ContainsKey("--normalize")) { Console.WriteLine(TextNormalizer.Normalize(text!)); return 0; }
    if (parsed.ContainsKey("--phonemes"))
    {
        Console.WriteLine(new EspeakPhonemizer(parsed.GetValueOrDefault("--espeak-library"), parsed.GetValueOrDefault("--espeak-data")).Phonemize(text!));
        return 0;
    }
    var speed = float.Parse(parsed.GetValueOrDefault("--speed", "1"), CultureInfo.InvariantCulture);
    if (!float.IsFinite(speed) || speed <= 0) throw new ArgumentException("--speed must be finite and positive.");
    Console.Error.WriteLine("Loading KittenTTS (the first run downloads the model and voices)...");
    using var model = await KittenTts.LoadAsync(new KittenTtsOptions
    {
        Model = parsed.GetValueOrDefault("--model", KittenTtsOptions.DefaultModel),
        Revision = parsed.GetValueOrDefault("--revision", "main"),
        CacheDirectory = parsed.GetValueOrDefault("--cache-dir"),
        ModelDirectory = parsed.GetValueOrDefault("--model-dir"),
        EspeakLibraryPath = parsed.GetValueOrDefault("--espeak-library"),
        EspeakDataPath = parsed.GetValueOrDefault("--espeak-data"),
        IntraOpNumThreads = int.Parse(parsed.GetValueOrDefault("--threads", "0"), CultureInfo.InvariantCulture)
    }, cancellation.Token);
    if (parsed.ContainsKey("--list-voices"))
    {
        Console.WriteLine(string.Join(Environment.NewLine, model.AvailableVoices));
        Console.WriteLine("Voice IDs: " + string.Join(", ", model.VoiceIds));
        return 0;
    }
    var watch = Stopwatch.StartNew();
    var audio = model.Generate(text!, parsed.GetValueOrDefault("--voice", "Jasper"), speed,
        !parsed.ContainsKey("--no-clean"), cancellation.Token);
    if (audio.Length == 0) throw new InvalidOperationException("No speech was generated. Check the input or try --no-clean.");
    var output = Path.GetFullPath(parsed.GetValueOrDefault("--output", "output.wav"));
    cancellation.Token.ThrowIfCancellationRequested();
    WaveFile.Write(output, audio);
    Console.WriteLine($"Saved {audio.Length / (double)KittenTts.SampleRate:F2}s of 24 kHz mono audio to {output} (generated in {watch.Elapsed.TotalSeconds:F2}s).");
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Canceled."); return 130; }
catch (Exception exception) { Console.Error.WriteLine($"Error: {exception.Message}"); return 1; }
