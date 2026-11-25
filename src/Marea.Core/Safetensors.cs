using System.Buffers.Binary;
using System.Text.Json;

namespace Marea.Core;

/// <summary>A tensor inside a safetensors file, with absolute byte offsets.</summary>
/// <param name="Name">Tensor name.</param>
/// <param name="Dtype">safetensors dtype string (F32, BF16, F16, ...).</param>
/// <param name="Shape">Dimensions.</param>
/// <param name="Begin">Absolute offset of the first byte.</param>
/// <param name="End">Absolute offset past the last byte.</param>
public sealed record TensorEntry(string Name, string Dtype, long[] Shape, long Begin, long End)
{
    /// <summary>Number of elements.</summary>
    public long Elements => Shape.Aggregate(1L, (a, b) => a * b);
    /// <summary>Size in bytes.</summary>
    public long Bytes => End - Begin;
}

/// <summary>Parsed safetensors header (tensors sorted by offset).</summary>
public sealed class SafetensorsFile
{
    private const long MaxHeader = 100L << 20;

    /// <summary>File path.</summary>
    public required string Path { get; init; }
    /// <summary>Total file size.</summary>
    public required long Size { get; init; }
    /// <summary>Raw bytes [0, HeaderEnd): length prefix plus JSON header.</summary>
    public required byte[] HeaderBytes { get; init; }
    /// <summary>Tensors sorted by Begin.</summary>
    public required List<TensorEntry> Tensors { get; init; }
    /// <summary>Offset where tensor data starts.</summary>
    public long HeaderEnd => HeaderBytes.Length;

    /// <summary>Reads and validates the header; returns null for non-safetensors or malformed files.</summary>
    public static SafetensorsFile? TryRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> len = stackalloc byte[8];
            if (fs.Read(len) != 8)
            {
                return null;
            }
            var hlen = (long)BinaryPrimitives.ReadUInt64LittleEndian(len);
            if (hlen <= 0 || hlen > MaxHeader || 8 + hlen > fs.Length)
            {
                return null;
            }
            var header = new byte[8 + hlen];
            len.CopyTo(header);
            fs.ReadExactly(header.AsSpan(8));
            using var doc = JsonDocument.Parse(header.AsMemory(8));
            var list = new List<TensorEntry>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Name == "__metadata__")
                {
                    continue;
                }
                var offs = p.Value.GetProperty("data_offsets");
                var shape = p.Value.GetProperty("shape").EnumerateArray().Select(x => x.GetInt64()).ToArray();
                list.Add(new TensorEntry(p.Name, p.Value.GetProperty("dtype").GetString()!, shape,
                    8 + hlen + offs[0].GetInt64(), 8 + hlen + offs[1].GetInt64()));
            }
            list.Sort((a, b) => a.Begin.CompareTo(b.Begin));
            return new SafetensorsFile { Path = path, Size = fs.Length, HeaderBytes = header, Tensors = list };
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException
                                      or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>Bit-exact float32 → bfloat16 conversion with round-to-nearest-even (PyTorch semantics).</summary>
public static class Bf16
{
    /// <summary>Converts little-endian F32 bytes into little-endian BF16 bytes (half the length).</summary>
    public static void FromF32(ReadOnlySpan<byte> f32, Span<byte> bf16)
    {
        var n = f32.Length / 4;
        for (var i = 0; i < n; i++)
        {
            var u = BinaryPrimitives.ReadUInt32LittleEndian(f32.Slice(i * 4, 4));
            ushort r;
            if ((u & 0x7F800000u) == 0x7F800000u && (u & 0x007FFFFFu) != 0)
            {
                r = 0x7FC0; // NaN, as torch does
            }
            else
            {
                r = (ushort)((u + 0x7FFFu + ((u >> 16) & 1u)) >> 16);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(bf16.Slice(i * 2, 2), r);
        }
    }
}
