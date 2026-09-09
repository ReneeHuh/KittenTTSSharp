# KittenTTSSharp

A .NET 8 C# port of [KittenML/KittenTTS](https://github.com/KittenML/KittenTTS)'s CPU inference pipeline. It uses the original ONNX models through ONNX Runtime and calls the native eSpeak NG library for English pronunciation. **Python is not required to build or run it.**

Includes model downloads and caching, offline loading, voice aliases and speed priors, English text processing, sentence chunk streaming, and 24 kHz mono WAV output. The source baseline is KittenTTS 0.8.1, revision `be5758500b731b8fc674acc62ea480d3022b7ebe`.

## Quick start on Windows x64

Run these commands from this repository's root (the directory containing `KittenTTSSharp.sln`). Install a .NET 8 or newer SDK first.

```powershell
./scripts/setup-espeak.ps1
dotnet build KittenTTSSharp.sln -c Release
dotnet run --project samples/KittenTTSSharp.Cli -c Release -- --text "Hello from C sharp." --voice Jasper --output hello.wav
```

The setup script downloads a checksum-verified copy of `espeakng-loader` 0.2.4 and extracts the native DLL and dictionaries under `.dependencies/espeak`. It does not install or invoke Python, change system settings, or require administrator access. The CLI discovers that directory when launched from the repository root.

The first synthesis run downloads `KittenML/kitten-tts-mini-0.8` and its voice data from Hugging Face. Later runs reuse the cache. Pass `--cache-dir .models` to keep models in the repository's ignored `.models` directory. Run with `--help` for all options.

## Use the library

Add a project reference to `src/KittenTTSSharp/KittenTTSSharp.csproj`, or build a local NuGet package with the command below. No package has been published by this repository yet.

```csharp
using KittenTTSSharp;

using var tts = await KittenTts.LoadAsync(new KittenTtsOptions
{
    Model = "KittenML/kitten-tts-mini-0.8",
    // Set these when eSpeak NG is outside the standard search locations:
    // EspeakLibraryPath = @"C:\path\to\espeak-ng.dll",
    // EspeakDataPath = @"C:\path\to\espeak-ng-data",
});

float[] audio = tts.Generate("Hello, world.", voice: "Jasper", speed: 1.0f);
WaveFile.Write("hello.wav", audio);

tts.GenerateToFile("The price is $12.50.", "price.wav", voice: "Luna");
Console.WriteLine(string.Join(", ", tts.AvailableVoices));
```

`Generate` and `GenerateStream` default to `cleanText: false`, matching the upstream public API. `GenerateToFile` and the CLI enable the synthesis preprocessor by default. The default library voice is `expr-voice-5-m`; the CLI defaults to `Jasper`. Voice aliases come from each model's configuration, so older models may expose only voice IDs.

```csharp
foreach (float[] chunk in tts.GenerateStream(
    "A first sentence. A second sentence.", voice: "Bella",
    cancellationToken: cancellationToken))
{
    // Send these mono float samples to your audio playback/output component at 24000 Hz.
}
```

Streaming yields a completed audio array for each text chunk. It is synchronous and does not stream individual samples during an ONNX inference call. Cancellation works between chunks and interrupts an active ONNX run. Reuse a `KittenTts` instance; dispose it when finished. Inference on one instance is serialized. eSpeak NG also uses a process-wide lock because its native state is global; use one eSpeak installation per process. Custom `IPhonemizer` instances remain owned by the caller.

## Text processing

The upstream project has two different text pipelines, both represented here:

```csharp
// Read-aloud normalization: abbreviations, citations, dates, times, numbers and URLs.
string spoken = TextNormalizer.Normalize("Dr. Rivera paid $12.50 at 3:05 p.m.");
// Doctor Rivera paid twelve dollars and fifty cents at three oh five p m.

NormalizedTextResult result = TextNormalizer.NormalizeWithSpans("Fig. 2");
// result.Text == "Figure two"; spans map each changed segment.

// The synthesis clean_text defaults: contractions, numbers, currency, units,
// scientific notation, fractions, phone/IP numbers, lowercase, and URL removal.
string cleaned = TextPreprocessor.Process("I can't carry 50kg.");

// Apply read-aloud normalization explicitly when that behavior is desired:
float[] normalizedAudio = tts.Generate(spoken, voice: "Jasper", cleanText: false);
```

Only English (`en` / `en-US`) is supported. Span offsets are UTF-16 string indices with exclusive end positions, as customary in .NET. The synthesis preprocessor ports upstream's default configuration; its separate optional NLP switches are not exposed.

## Models and offline use

The loader accepts `ONNX1` and `ONNX2` configurations with `input_ids`, `style`, and `speed` inputs. Mini 0.8 and nano 0.2 have been exercised with real inference on Windows x64. Other variants using the same schema can be selected with `Model` or `--model`; they have not all been validated here.

```csharp
using var offline = await KittenTts.LoadAsync(new KittenTtsOptions
{
    ModelDirectory = @"C:\models\kitten-tts-mini-0.8",
    IntraOpNumThreads = 2
});
```

The directory must contain `config.json` and the ONNX/NPZ files named in that configuration. `ModelDirectory` prevents network access. Downloads use temporary files and publish completed files into the cache. The default cache is `KittenTTSSharp/models` below .NET's local application-data directory, organized by repository and revision. Set `Revision` to a Hugging Face commit for a reproducible model version. Cached files are reused without checking for remote updates; if a cache file is damaged, remove that model's cache directory and download it again.

## Native dependencies and platforms

- Windows x64: use the setup script or install eSpeak NG separately.
- Linux: install `libespeak-ng1` and its data package, for example `sudo apt install libespeak-ng1 espeak-ng-data` on Debian/Ubuntu.
- macOS: install eSpeak NG, for example `brew install espeak-ng`.

Set `ESPEAK_LIBRARY_PATH` to the native library and `ESPEAK_DATA_PATH` to `espeak-ng-data` (or its parent) when automatic discovery does not find them. Options supplied in C# or through CLI arguments take precedence over environment variables. For deployment, you can place the native library and its `espeak-ng-data` directory together under an `espeak` directory next to your application. Match the library's architecture to your .NET process.

The code uses cross-platform .NET APIs; local end-to-end validation was performed on Windows x64. Linux/macOS inference, GPU acceleration, mobile, browser, and Native AOT deployment have not been validated. This package uses the CPU ONNX Runtime distribution.

## Build and test

```powershell
dotnet build KittenTTSSharp.sln -c Release
dotnet test KittenTTSSharp.sln -c Release
dotnet pack src/KittenTTSSharp/KittenTTSSharp.csproj -c Release -o artifacts/packages
```

The normal test run uses checked-in Python reference fixtures and synthetic binary-format cases. It does not download models or require eSpeak/Python. Native tests are skipped unless enabled:

```powershell
$env:ESPEAK_LIBRARY_PATH = (Resolve-Path .dependencies/espeak/espeakng_loader/espeak-ng.dll).Path
$env:ESPEAK_DATA_PATH = (Resolve-Path .dependencies/espeak/espeakng_loader/espeak-ng-data).Path
$env:KITTENTTS_TEST_NATIVE = '1'
$env:KITTENTTS_TEST_MODEL = 'C:\path\to\downloaded\model-directory'
dotnet test KittenTTSSharp.sln -c Release
```

The native token fixtures use `espeakng-loader` 0.2.4 and `phonemizer` 3.4.0. Different eSpeak versions can produce different pronunciations. Development-only Python scripts in `scripts/` regenerate reference fixtures from an upstream checkout and compare reference audio; they are excluded from the library package and are not runtime dependencies.

There are deliberate edge-case fixes relative to upstream: punctuation without numbers does not crash read-aloud normalization, enormous numbers are not silently truncated, and oversized words are split without breaking surrogate pairs. Chunk length follows upstream's 400-character default, with a possible additional terminal comma.

## License

This port is Apache-2.0; see [LICENSE](LICENSE) and [NOTICE](NOTICE). Model weights retain their own licenses. ONNX Runtime is MIT. The separately installed [eSpeak NG](https://github.com/espeak-ng/espeak-ng) dependency is GPL-3.0-or-later and is not bundled in this project's NuGet package. Its Windows distribution is obtained through [espeakng-loader](https://github.com/thewh1teagle/espeakng-loader).
