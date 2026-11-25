using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Marea.Tests;

/// <summary>Helpers that write small, valid safetensors files and temporary folders.</summary>
internal static class TestFiles
{
    /// <summary>Writes a safetensors file with the given tensors (name, dtype, shape, raw bytes).</summary>
    public static void WriteSafetensors(string path, params (string Name, string Dtype, long[] Shape, byte[] Data)[] tensors)
    {
        var header = new Dictionary<string, object>();
        long off = 0;
        foreach (var t in tensors)
        {
            header[t.Name] = new { dtype = t.Dtype, shape = t.Shape, data_offsets = new[] { off, off + t.Data.Length } };
            off += t.Data.Length;
        }
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header));
        var pad = (8 - json.Length % 8) % 8;
        json = [.. json, .. Enumerable.Repeat((byte)' ', pad)];
        using var fs = File.Create(path);
        var len = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(len, (ulong)json.Length);
        fs.Write(len);
        fs.Write(json);
        foreach (var t in tensors)
        {
            fs.Write(t.Data);
        }
    }

    /// <summary>Random float32 bytes for <paramref name="n"/> elements (finite values).</summary>
    public static byte[] RandomF32(int n, int seed)
    {
        var rnd = new Random(seed);
        var b = new byte[n * 4];
        for (var i = 0; i < n; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(i * 4), (float)(rnd.NextDouble() * 4 - 2));
        }
        return b;
    }

    /// <summary>A fresh temporary directory.</summary>
    public static string TempDir(string tag)
    {
        var d = Path.Combine(Path.GetTempPath(), $"marea-{tag}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(d);
        return d;
    }
}
