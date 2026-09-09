using System.Text;

namespace KittenTTSSharp;

public static class WaveFile
{
    /// <summary>Write mono 16-bit PCM WAV. Values outside [-1, 1] are clipped.</summary>
    public static void Write(string path, ReadOnlySpan<float> samples, int sampleRate = KittenTts.SampleRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (sampleRate <= 0 || sampleRate > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var byteCount = checked(samples.Length * 2);
        var riffLength = checked(byteCount + 36);
        foreach (var sample in samples)
            if (!float.IsFinite(sample)) throw new ArgumentException("Audio contains nonfinite samples.", nameof(samples));
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write("RIFF"u8);
        writer.Write(riffLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(byteCount);
        foreach (var sample in samples)
            writer.Write((short)Math.Clamp(MathF.Floor(sample * 32768), short.MinValue, short.MaxValue));
    }
}
