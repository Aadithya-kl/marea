using System.Text;

namespace Marea.Core;

/// <summary>Header of a GGUF file (llama.cpp, Ollama, LM Studio).</summary>
/// <param name="Version">GGUF version.</param>
/// <param name="Architecture">general.architecture.</param>
/// <param name="Name">general.name.</param>
/// <param name="FileType">Quantization name from general.file_type (e.g. Q4_K_M).</param>
/// <param name="Parameters">Sum of the elements of every tensor.</param>
/// <param name="ChatTemplate">tokenizer.chat_template (Jinja), if any.</param>
public sealed record GgufInfo(int Version, string? Architecture, string? Name, string? FileType, long Parameters, string? ChatTemplate);

/// <summary>Reads the metadata and tensor table of a GGUF file without loading any weights.
/// Bounded everywhere: a malformed or hostile header returns null instead of exhausting memory.</summary>
public static class GgufReader
{
    /// <summary>"GGUF" read as a little-endian uint32.</summary>
    public const uint Magic = 0x46554747;

    private const long MaxString = 16L << 20;
    private const long MaxCount = 100_000_000;

    // llama_ftype values (llama.h). Unknown values are shown as "ftype N".
    private static readonly Dictionary<long, string> FileTypes = new()
    {
        [0] = "F32", [1] = "F16", [2] = "Q4_0", [3] = "Q4_1", [7] = "Q8_0", [8] = "Q5_0", [9] = "Q5_1",
        [10] = "Q2_K", [11] = "Q3_K_S", [12] = "Q3_K_M", [13] = "Q3_K_L", [14] = "Q4_K_S", [15] = "Q4_K_M",
        [16] = "Q5_K_S", [17] = "Q5_K_M", [18] = "Q6_K", [19] = "IQ2_XXS", [20] = "IQ2_XS", [21] = "Q2_K_S",
        [22] = "IQ3_XS", [23] = "IQ3_XXS", [24] = "IQ1_S", [25] = "IQ4_NL", [26] = "IQ3_S", [27] = "IQ3_M",
        [28] = "IQ2_S", [29] = "IQ2_M", [30] = "IQ4_XS", [31] = "IQ1_M", [32] = "BF16", [36] = "TQ1_0", [37] = "TQ2_0",
    };

    // Byte size of the fixed-size GGUF value types (index = type id); 0 = variable (string, array).
    private static readonly int[] FixedSize = [1, 1, 2, 2, 4, 4, 4, 1, 0, 0, 8, 8, 8];

    /// <summary>Reads the header; null when the file is not GGUF or the header is malformed.</summary>
    public static GgufInfo? TryRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            return Read(fs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                      or OverflowException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Reads a GGUF header from a stream positioned at its start.</summary>
    /// <exception cref="InvalidDataException">The header is malformed or exceeds the safety limits.</exception>
    public static GgufInfo? Read(Stream s)
    {
        using var r = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != Magic)
        {
            return null;
        }
        var version = (int)r.ReadUInt32();
        if (version is < 2 or > 3)
        {
            return new GgufInfo(version, null, null, null, 0, null);
        }
        var tensors = Count(r.ReadUInt64());
        var kvs = Count(r.ReadUInt64());
        string? arch = null, name = null, ftype = null, template = null;
        for (long i = 0; i < kvs; i++)
        {
            var key = ReadString(r);
            var type = r.ReadUInt32();
            switch (key)
            {
                case "general.architecture" when type == 8:
                    arch = ReadString(r);
                    break;
                case "general.name" when type == 8:
                    name = ReadString(r);
                    break;
                case "tokenizer.chat_template" when type == 8:
                    template = ReadString(r);
                    break;
                case "general.file_type" when type is 4 or 5:
                    var ft = r.ReadUInt32();
                    ftype = FileTypes.TryGetValue(ft, out var n) ? n : $"ftype {ft}";
                    break;
                default:
                    Skip(r, type);
                    break;
            }
        }
        long parameters = 0;
        for (long i = 0; i < tensors; i++)
        {
            SkipBytes(r, Count(r.ReadUInt64(), MaxString));
            var dims = r.ReadUInt32();
            if (dims > 8)
            {
                throw new InvalidDataException("too many tensor dimensions");
            }
            long elements = 1;
            for (var d = 0; d < dims; d++)
            {
                elements = checked(elements * Count(r.ReadUInt64(), long.MaxValue));
            }
            r.ReadUInt32(); // tensor type
            r.ReadUInt64(); // offset
            parameters = checked(parameters + elements);
        }
        return new GgufInfo(version, arch, name, ftype, parameters, template);
    }

    private static long Count(ulong v, long max = MaxCount) =>
        v > (ulong)max ? throw new InvalidDataException("count out of range") : (long)v;

    private static string ReadString(BinaryReader r)
    {
        var len = Count(r.ReadUInt64(), MaxString);
        var bytes = r.ReadBytes((int)len);
        return bytes.Length == len ? Encoding.UTF8.GetString(bytes) : throw new EndOfStreamException();
    }

    private static void Skip(BinaryReader r, uint type)
    {
        if (type == 8)
        {
            SkipBytes(r, Count(r.ReadUInt64(), MaxString));
        }
        else if (type == 9)
        {
            var inner = r.ReadUInt32();
            var n = Count(r.ReadUInt64());
            if (inner == 9)
            {
                throw new InvalidDataException("nested arrays are not supported");
            }
            if (inner == 8)
            {
                for (long i = 0; i < n; i++)
                {
                    SkipBytes(r, Count(r.ReadUInt64(), MaxString));
                }
            }
            else
            {
                SkipBytes(r, checked(n * Size(inner)));
            }
        }
        else
        {
            SkipBytes(r, Size(type));
        }
    }

    private static int Size(uint type) =>
        type < FixedSize.Length && FixedSize[type] > 0 ? FixedSize[type] : throw new InvalidDataException("bad value type");

    private static void SkipBytes(BinaryReader r, long n)
    {
        var s = r.BaseStream;
        if (s.CanSeek)
        {
            if (s.Position + n > s.Length)
            {
                throw new EndOfStreamException();
            }
            s.Seek(n, SeekOrigin.Current);
            return;
        }
        Span<byte> buf = stackalloc byte[4096];
        while (n > 0)
        {
            var got = s.Read(buf[..(int)Math.Min(buf.Length, n)]);
            if (got == 0)
            {
                throw new EndOfStreamException();
            }
            n -= got;
        }
    }
}
