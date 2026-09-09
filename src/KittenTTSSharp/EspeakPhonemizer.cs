using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace KittenTTSSharp;

/// <summary>English IPA pronunciation using a separately installed eSpeak NG library.</summary>
public sealed class EspeakPhonemizer : IPhonemizer
{
    private const string Punctuation = ";:,.!?¡¿—…\"«»“”(){}[]";
    private static readonly object Gate = new();
    private static NativeApi? sharedApi;
    private readonly NativeApi api;

    public EspeakPhonemizer(string? libraryPath = null, string? dataPath = null)
    {
        libraryPath ??= Environment.GetEnvironmentVariable("ESPEAK_LIBRARY_PATH");
        dataPath ??= Environment.GetEnvironmentVariable("ESPEAK_DATA_PATH");
        lock (Gate)
        {
            if (sharedApi is null) sharedApi = new NativeApi(libraryPath, dataPath);
            else if ((libraryPath is not null && Path.GetFullPath(libraryPath) != sharedApi.LibraryPath) ||
                     (dataPath is not null && NativeApi.NormalizeDataPath(dataPath) != sharedApi.DataPath))
                throw new InvalidOperationException("eSpeak NG is already initialized with different paths. Use one eSpeak installation per process.");
            api = sharedApi;
        }
    }

    public string Phonemize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\0')) throw new ArgumentException("Text must not contain NUL characters.", nameof(text));
        lock (Gate)
        {
            // eSpeak holds process-global state. Serialize calls and explicitly select the voice.
            if (api.SetVoice("en-us") != 0) throw new InvalidOperationException("eSpeak NG's en-us voice is unavailable. Check its data directory.");
            var result = new StringBuilder();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (!Punctuation.Contains(text[i])) continue;
                if (text[i] is '.' or ',' && i > 0 && i + 1 < text.Length &&
                    char.IsAsciiDigit(text[i - 1]) && char.IsAsciiDigit(text[i + 1])) continue;
                AppendSpeech(text[start..i], result);
                result.Append(text[i]);
                start = i + 1;
            }
            AppendSpeech(text[start..], result);
            return Regex.Replace(result.ToString(), @"\s+", " ").Trim();
        }
    }

    private void AppendSpeech(string text, StringBuilder output)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (text.Length > 0) output.Append(' ');
            return;
        }
        var allocation = Marshal.StringToCoTaskMemUTF8(text.Trim());
        try
        {
            var cursor = allocation;
            while (cursor != IntPtr.Zero)
            {
                var before = cursor;
                var pointer = api.TextToPhonemes(ref cursor, 1, 2);
                if (pointer != IntPtr.Zero)
                {
                    var phonemes = Marshal.PtrToStringUTF8(pointer) ?? "";
                    if (output.Length > 0 && !char.IsWhiteSpace(output[^1])) output.Append(' ');
                    output.Append(phonemes.Replace("_", "").Trim());
                }
                if (cursor == before) throw new InvalidOperationException("eSpeak NG did not advance through the input.");
            }
            output.Append(' ');
        }
        finally { Marshal.FreeCoTaskMem(allocation); }
    }

    private sealed class NativeApi
    {
        // Deliberately retain the native handle for process lifetime: the library owns global dictionaries.
        private readonly IntPtr handle;
        public string LibraryPath { get; }
        public string? DataPath { get; }
        public SetVoiceDelegate SetVoice { get; }
        public TextToPhonemesDelegate TextToPhonemes { get; }

        public NativeApi(string? libraryPath, string? dataPath)
        {
            var names = OperatingSystem.IsWindows() ? new[] { "espeak-ng.dll", "libespeak-ng.dll" } :
                OperatingSystem.IsMacOS() ? new[] { "libespeak-ng.dylib", "/opt/homebrew/lib/libespeak-ng.dylib", "/usr/local/lib/libespeak-ng.dylib" } :
                new[] { "libespeak-ng.so.1", "libespeak-ng.so" };
            var roots = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "espeak"),
                Path.Combine(Environment.CurrentDirectory, ".dependencies", "espeak", "espeakng_loader"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "eSpeak NG"),
                AppContext.BaseDirectory
            };
            var candidates = libraryPath is null
                ? roots.SelectMany(root => names.Where(n => !Path.IsPathRooted(n)).Select(n => Path.Combine(root, n))).Concat(names)
                : [Path.GetFullPath(libraryPath)];
            string? loadedPath = null;
            foreach (var candidate in candidates)
            {
                if (!NativeLibrary.TryLoad(candidate, out handle)) continue;
                loadedPath = Path.IsPathRooted(candidate) ? Path.GetFullPath(candidate) : candidate;
                break;
            }
            if (loadedPath is null)
                throw new DllNotFoundException("eSpeak NG could not be loaded. On Windows run scripts/setup-espeak.ps1 from the repository root; on Linux install libespeak-ng1; on macOS install espeak-ng. Alternatively set ESPEAK_LIBRARY_PATH and ESPEAK_DATA_PATH.");
            LibraryPath = loadedPath;
            try
            {
                var adjacentData = Path.Combine(Path.GetDirectoryName(loadedPath) ?? "", "espeak-ng-data");
                DataPath = NormalizeDataPath(dataPath ?? (Directory.Exists(adjacentData) ? adjacentData : null));
                var initialize = Marshal.GetDelegateForFunctionPointer<InitializeDelegate>(NativeLibrary.GetExport(handle, "espeak_Initialize"));
                SetVoice = Marshal.GetDelegateForFunctionPointer<SetVoiceDelegate>(NativeLibrary.GetExport(handle, "espeak_SetVoiceByName"));
                TextToPhonemes = Marshal.GetDelegateForFunctionPointer<TextToPhonemesDelegate>(NativeLibrary.GetExport(handle, "espeak_TextToPhonemes"));
                if (initialize(2, 0, DataPath, 0x8000) < 0)
                    throw new InvalidOperationException("eSpeak NG initialization failed. Check ESPEAK_DATA_PATH.");
            }
            catch { NativeLibrary.Free(handle); throw; }
        }

        public static string? NormalizeDataPath(string? path)
        {
            if (path is null) return null;
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (Path.GetFileName(path) == "espeak-ng-data") path = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(Path.Combine(path, "espeak-ng-data")))
                throw new DirectoryNotFoundException($"No espeak-ng-data directory found under '{path}'.");
            return path;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeDelegate(int output, int bufferLength, [MarshalAs(UnmanagedType.LPUTF8Str)] string? path, int options);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SetVoiceDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr TextToPhonemesDelegate(ref IntPtr text, int textMode, int phonemeMode);
    }
}
