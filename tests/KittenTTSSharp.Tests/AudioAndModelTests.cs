using System.IO.Compression;
using System.Text;

namespace KittenTTSSharp.Tests;

public sealed class AudioAndModelTests
{
    [Theory]
    [InlineData("<f2", 1)]
    [InlineData(">f2", 2)]
    [InlineData("<f4", 2)]
    [InlineData(">f4", 3)]
    [InlineData("<f8", 3)]
    [InlineData(">f8", 1)]
    public void ReadNumpyFloatFormats(string dtype, byte version)
    {
        using var stream = Npy(dtype, version);
        var voice = VoiceArchive.ReadNpy(stream);
        Assert.Equal(2, voice.Rows);
        Assert.Equal(2, voice.Width);
        Assert.Equal(new float[] { 0.25f, -0.5f, 1, 2 }, voice.Samples);
        Assert.Equal(new float[] { 1, 2 }, voice.GetStyle(500));
        Assert.Equal(new float[] { 0.25f, -0.5f }, voice.GetStyle(0));
    }

    [Fact]
    public void RejectInvalidNumpyLayoutAndTruncation()
    {
        using var fortran = Npy("<f4", 1, fortran: true);
        Assert.Throws<InvalidDataException>(() => VoiceArchive.ReadNpy(fortran));
        using var objectArray = Npy("|O8", 1);
        Assert.Throws<InvalidDataException>(() => VoiceArchive.ReadNpy(objectArray));
        using var valid = Npy("<f4", 1);
        using var truncated = new MemoryStream(valid.ToArray()[..^1]);
        Assert.Throws<EndOfStreamException>(() => VoiceArchive.ReadNpy(truncated));
    }

    [Fact]
    public void LoadsCompressedNpzWithVoiceNames()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var file = File.Create(path))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            using (var entry = zip.CreateEntry("expr-voice-2-m.npy").Open())
            using (var npy = Npy("<f4", 1)) npy.CopyTo(entry);
            var voices = VoiceArchive.Load(path);
            Assert.Single(voices);
            Assert.Equal(new float[] { 1, 2 }, voices["expr-voice-2-m"].GetStyle(12));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("../model.onnx")]
    [InlineData("..\\voices.npz")]
    [InlineData("C:\\model.onnx")]
    [InlineData("/model.onnx")]
    [InlineData(".")]
    [InlineData("")]
    public void ConfigCannotEscapeModelDirectory(string name) => Assert.Throws<InvalidDataException>(() => ModelConfig.ValidateFileName(name));

    [Fact]
    public void RejectsUnknownModelAndInvalidSpeedPrior()
    {
        Assert.Throws<NotSupportedException>(() => new ModelConfig { Type = "PYTORCH" }.Validate());
        Assert.Throws<InvalidDataException>(() => new ModelConfig
        {
            Type = "ONNX2", ModelFile = "model.onnx", Voices = "voices.npz", SpeedPriors = new() { ["voice"] = float.NaN }
        }.Validate());
    }

    [Fact]
    public void WaveHeaderAndPcmSamplesAreStandard()
    {
        var path = Path.GetTempFileName();
        try
        {
            WaveFile.Write(path, [-2, -0.5f, 0, 0.5f, 2]);
            using var reader = new BinaryReader(File.OpenRead(path));
            Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
            Assert.Equal(46, reader.ReadInt32());
            Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(reader.ReadBytes(8)));
            Assert.Equal(16, reader.ReadInt32());
            Assert.Equal(1, reader.ReadInt16());
            Assert.Equal(1, reader.ReadInt16());
            Assert.Equal(24000, reader.ReadInt32());
            Assert.Equal(48000, reader.ReadInt32());
            Assert.Equal(2, reader.ReadInt16());
            Assert.Equal(16, reader.ReadInt16());
            Assert.Equal("data", Encoding.ASCII.GetString(reader.ReadBytes(4)));
            Assert.Equal(10, reader.ReadInt32());
            Assert.Equal(new short[] { -32768, -16384, 0, 16384, 32767 }, Enumerable.Range(0, 5).Select(_ => reader.ReadInt16()));
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidAudioDoesNotOverwriteExistingFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "existing");
            Assert.Throws<ArgumentException>(() => WaveFile.Write(path, [float.NaN]));
            Assert.Throws<ArgumentOutOfRangeException>(() => WaveFile.Write(path, [0], 0));
            Assert.Equal("existing", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private static MemoryStream Npy(string dtype, byte version, bool fortran = false)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[] { 0x93, 78, 85, 77, 80, 89, version, 0 });
        var header = $"{{'descr': '{dtype}', 'fortran_order': {(fortran ? "True" : "False")}, 'shape': (2, 2), }}";
        var preambleLength = version == 1 ? 10 : 12;
        header = header.PadRight(header.Length + (64 - ((header.Length + preambleLength + 1) % 64)) % 64) + "\n";
        if (version == 1) writer.Write((ushort)header.Length); else writer.Write((uint)header.Length);
        writer.Write(Encoding.ASCII.GetBytes(header));
        foreach (var value in new[] { 0.25f, -0.5f, 1, 2 })
        {
            var bytes = dtype[2] switch
            {
                '2' => BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)value)),
                '4' => BitConverter.GetBytes(value),
                _ => BitConverter.GetBytes((double)value)
            };
            if (dtype[0] == '>') Array.Reverse(bytes);
            writer.Write(bytes);
        }
        stream.Position = 0;
        return stream;
    }
}
