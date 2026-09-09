namespace KittenTTSSharp;

public sealed class KittenTtsOptions
{
    public const string DefaultModel = "KittenML/kitten-tts-mini-0.8";
    public string Model { get; init; } = DefaultModel;
    public string Revision { get; init; } = "main";
    public string? CacheDirectory { get; init; }
    /// <summary>Use a directory containing config.json and its model/voices files, without networking.</summary>
    public string? ModelDirectory { get; init; }
    public string? EspeakLibraryPath { get; init; }
    /// <summary>The espeak-ng-data directory or its parent.</summary>
    public string? EspeakDataPath { get; init; }
    public IPhonemizer? Phonemizer { get; init; }
    public int IntraOpNumThreads { get; init; }
    public int MaxChunkLength { get; init; } = 400;
}

public interface IPhonemizer
{
    /// <summary>Return en-US IPA phonemes with stress and punctuation preserved.</summary>
    string Phonemize(string text);
}
