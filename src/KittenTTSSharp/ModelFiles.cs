using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KittenTTSSharp;

internal sealed class ModelConfig
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("model_file")] public string ModelFile { get; init; } = "";
    [JsonPropertyName("voices")] public string Voices { get; init; } = "";
    [JsonPropertyName("speed_priors")] public Dictionary<string, float> SpeedPriors { get; init; } = [];
    [JsonPropertyName("voice_aliases")] public Dictionary<string, string> VoiceAliases { get; init; } = [];

    public void Validate()
    {
        if (Type is not ("ONNX1" or "ONNX2"))
            throw new NotSupportedException($"Unsupported KittenTTS model type '{Type}'. Expected ONNX1 or ONNX2.");
        ValidateFileName(ModelFile);
        ValidateFileName(Voices);
        if (SpeedPriors is null || VoiceAliases is null || SpeedPriors.Values.Any(v => !float.IsFinite(v) || v <= 0))
            throw new InvalidDataException("Invalid voice aliases or speed priors in config.json.");
    }

    internal static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Model config filenames must be plain filenames within the model directory.");
    }
}

internal sealed record ModelFiles(string Directory, ModelConfig Config)
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };

    public static async Task<ModelFiles> LoadAsync(KittenTtsOptions options, CancellationToken cancellationToken)
    {
        string directory;
        string? baseUrl = null;
        if (options.ModelDirectory is not null)
        {
            directory = Path.GetFullPath(options.ModelDirectory);
        }
        else
        {
            var repo = options.Model.Contains('/') ? options.Model : "KittenML/" + options.Model;
            if (!Regex.IsMatch(repo, @"\A[A-Za-z0-9_-][A-Za-z0-9_.-]*/[A-Za-z0-9_-][A-Za-z0-9_.-]*\z") ||
                string.IsNullOrWhiteSpace(options.Revision))
                throw new ArgumentException("Model must be a Hugging Face repository ID and revision must be nonempty.");
            var cache = options.CacheDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KittenTTSSharp", "models");
            var revisionKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.Revision)))[..16];
            directory = Path.GetFullPath(Path.Combine(cache, repo.Replace('/', Path.DirectorySeparatorChar), revisionKey));
            System.IO.Directory.CreateDirectory(directory);
            baseUrl = $"https://huggingface.co/{repo}/resolve/{Uri.EscapeDataString(options.Revision)}/";
            await DownloadAsync(baseUrl, directory, "config.json", cancellationToken).ConfigureAwait(false);
        }

        var json = await File.ReadAllTextAsync(Path.Combine(directory, "config.json"), cancellationToken).ConfigureAwait(false);
        var config = JsonSerializer.Deserialize<ModelConfig>(json) ?? throw new InvalidDataException("Empty model config.");
        config.Validate();
        foreach (var filename in new[] { config.ModelFile, config.Voices })
        {
            if (baseUrl is not null)
                await DownloadAsync(baseUrl, directory, filename, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(Path.Combine(directory, filename)))
                throw new FileNotFoundException($"Model file is missing: {filename}", Path.Combine(directory, filename));
        }
        return new ModelFiles(directory, config);
    }

    private static async Task DownloadAsync(string baseUrl, string directory, string filename, CancellationToken ct)
    {
        var destination = Path.Combine(directory, filename);
        if (File.Exists(destination) && new FileInfo(destination).Length > 0) return;
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var response = await Client.GetAsync(baseUrl + Uri.EscapeDataString(filename), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            var size = new FileInfo(temporary).Length;
            if (size == 0 || (response.Content.Headers.ContentLength is long expected && size != expected))
                throw new InvalidDataException($"Incomplete download of {filename}.");
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
