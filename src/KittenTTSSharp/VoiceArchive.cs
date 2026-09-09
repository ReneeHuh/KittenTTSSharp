using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KittenTTSSharp;

internal sealed record VoiceData(float[] Samples, int Rows, int Width)
{
    public float[] GetStyle(int textLength)
    {
        var row = Math.Min(textLength, Rows - 1);
        return Samples.AsSpan(row * Width, Width).ToArray();
    }
}

internal static class VoiceArchive
{
    // NPZ is a ZIP of NPY arrays. Only numeric, C-order 2-D styles are accepted.
    public static Dictionary<string, VoiceData> Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var result = new Dictionary<string, VoiceData>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".npy", StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            if (!result.TryAdd(Path.GetFileNameWithoutExtension(entry.FullName), ReadNpy(stream)))
                throw new InvalidDataException("Duplicate voice in NPZ archive.");
        }
        if (result.Count == 0) throw new InvalidDataException("No voices found in the NPZ archive.");
        return result;
    }

    internal static VoiceData ReadNpy(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(6).AsSpan().SequenceEqual(new byte[] { 0x93, 78, 85, 77, 80, 89 }))
            throw new InvalidDataException("Invalid NPY signature.");
        var major = reader.ReadByte();
        var minor = reader.ReadByte();
        if (major is < 1 or > 3 || minor != 0) throw new InvalidDataException("Unsupported NPY version.");
        var headerLength = major == 1 ? reader.ReadUInt16() : reader.ReadUInt32();
        if (headerLength > 65536) throw new InvalidDataException("NPY header is too large.");
        var headerBytes = reader.ReadBytes((int)headerLength);
        if (headerBytes.Length != headerLength) throw new EndOfStreamException("Truncated NPY header.");
        var header = (major == 3 ? Encoding.UTF8 : Encoding.Latin1).GetString(headerBytes);
        var dtype = Regex.Match(header, "['\"]descr['\"]\\s*:\\s*['\"]([<>=|]f[248])['\"]");
        var shape = Regex.Match(header, "['\"]shape['\"]\\s*:\\s*\\(\\s*(\\d+)\\s*,\\s*(\\d+)\\s*,?\\s*\\)");
        if (!dtype.Success || !shape.Success || !Regex.IsMatch(header, "['\"]fortran_order['\"]\\s*:\\s*False"))
            throw new InvalidDataException("Voice arrays must be 2-D, C-order float16, float32, or float64 NPY arrays.");
        if (!int.TryParse(shape.Groups[1].Value, out var rows) || !int.TryParse(shape.Groups[2].Value, out var width) ||
            rows <= 0 || width <= 0 || (long)rows * width > 16_000_000)
            throw new InvalidDataException("Invalid or oversized voice array shape.");
        var format = dtype.Groups[1].Value;
        var bigEndian = format[0] == '>' || (format[0] == '=' && !BitConverter.IsLittleEndian);
        var size = format[2] - '0';
        var samples = new float[rows * width];
        Span<byte> bytes = stackalloc byte[8];
        for (var i = 0; i < samples.Length; i++)
        {
            stream.ReadExactly(bytes[..size]);
            if (bigEndian) bytes[..size].Reverse();
            samples[i] = size switch
            {
                2 => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes)),
                4 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
                8 => (float)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes)),
                _ => throw new InvalidDataException("Unsupported dtype.")
            };
            if (!float.IsFinite(samples[i])) throw new InvalidDataException("Voice array contains nonfinite values.");
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing NPY data.");
        return new VoiceData(samples, rows, width);
    }
}
