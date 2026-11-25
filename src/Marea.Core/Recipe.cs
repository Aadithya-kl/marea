using System.Security.Cryptography;
using System.Text.Json;

namespace Marea.Core;

/// <summary>Deterministic description of how to rebuild a safetensors file from tensors of
/// other local files: header bytes, then contiguous segments (raw copy or F32→BF16 cast).</summary>
public sealed class Recipe
{
    /// <summary>Format version.</summary>
    public int Version { get; set; } = 1;
    /// <summary>sha256 of the rebuilt file.</summary>
    public string TargetSha { get; set; } = "";
    /// <summary>Size of the rebuilt file.</summary>
    public long TargetSize { get; set; }
    /// <summary>Header bytes (length prefix + JSON), base64.</summary>
    public string HeaderBase64 { get; set; } = "";
    /// <summary>Source files with the fingerprint they had when the recipe was verified.</summary>
    public List<RecipeSource> Sources { get; set; } = [];
    /// <summary>Contiguous data segments covering [header end, TargetSize).</summary>
    public List<RecipeSegment> Segments { get; set; } = [];

    /// <summary>Serializes to JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Parses JSON produced by <see cref="ToJson"/>.</summary>
    public static Recipe FromJson(string json) => JsonSerializer.Deserialize<Recipe>(json)!;

    /// <summary>True when every source still has the size and write time it had at verification.</summary>
    public bool SourcesUnchanged() => Sources.All(s =>
    {
        var fi = new FileInfo(s.Path);
        return fi.Exists && fi.Length == s.Size && fi.LastWriteTimeUtc.Ticks == s.MTimeTicks;
    });

    /// <summary>Coded summary (render with <see cref="Loc.Render"/>).</summary>
    public string Describe()
    {
        var casts = Segments.Count(x => x.Op == RecipeOp.CastF32ToBf16);
        var folder = Sources.Count > 0 ? Path.GetFileName(Path.GetDirectoryName(Sources[0].Path)) : "";
        return casts > 0 ? Loc.Code("src.recipe_cast", folder) : Loc.Code("src.recipe_compose", folder);
    }
}

/// <summary>A file the recipe reads from.</summary>
public sealed record RecipeSource(string Path, long Size, long MTimeTicks);

/// <summary>Segment operation.</summary>
public enum RecipeOp
{
    /// <summary>Copy bytes verbatim.</summary>
    Copy = 0,
    /// <summary>Read F32 and write BF16 with round-to-nearest-even.</summary>
    CastF32ToBf16 = 1,
}

/// <summary>Target range [Offset, Offset+Length) produced from a source file.</summary>
public sealed record RecipeSegment(long Offset, long Length, RecipeOp Op, int Source, long SourceOffset);

/// <summary>Builds recipes by matching tensor names and shapes across safetensors files.</summary>
public static class RecipeBuilder
{
    /// <summary>Tries to express <paramref name="target"/> entirely with tensors of <paramref name="candidates"/>.
    /// Returns null when any tensor has no exact counterpart. The result is <b>not yet verified</b>.</summary>
    public static Recipe? TryBuild(SafetensorsFile target, string targetSha, IReadOnlyList<SafetensorsFile> candidates)
    {
        var pool = candidates.Where(c => !string.Equals(c.Path, target.Path, StringComparison.OrdinalIgnoreCase)).ToList();
        var index = new Dictionary<string, (int File, TensorEntry T)>();
        for (var i = 0; i < pool.Count; i++)
        {
            foreach (var t in pool[i].Tensors)
            {
                index.TryAdd(t.Name, (i, t));
            }
        }
        var used = new Dictionary<int, int>();
        var segs = new List<RecipeSegment>();
        var pos = target.HeaderEnd;
        foreach (var t in target.Tensors)
        {
            if (t.Begin != pos || !index.TryGetValue(t.Name, out var src) || !src.T.Shape.SequenceEqual(t.Shape))
            {
                return null;
            }
            RecipeOp op;
            if (t.Dtype == src.T.Dtype && t.Bytes == src.T.Bytes)
            {
                op = RecipeOp.Copy;
            }
            else if (t.Dtype == "BF16" && src.T.Dtype == "F32" && t.Bytes * 2 == src.T.Bytes)
            {
                op = RecipeOp.CastF32ToBf16;
            }
            else
            {
                return null;
            }
            if (!used.TryGetValue(src.File, out var si))
            {
                si = used.Count;
                used[src.File] = si;
            }
            segs.Add(new RecipeSegment(t.Begin, t.Bytes, op, si, src.T.Begin));
            pos = t.End;
        }
        if (pos != target.Size || segs.Count == 0)
        {
            return null;
        }
        var sources = used.OrderBy(kv => kv.Value).Select(kv =>
        {
            var fi = new FileInfo(pool[kv.Key].Path);
            return new RecipeSource(fi.FullName, fi.Length, fi.LastWriteTimeUtc.Ticks);
        }).ToList();
        return new Recipe
        {
            TargetSha = targetSha,
            TargetSize = target.Size,
            HeaderBase64 = Convert.ToBase64String(target.HeaderBytes),
            Sources = sources,
            Segments = segs,
        };
    }

    /// <summary>Cheap pre-check: compares up to 1 MiB at the start of up to 12 evenly spaced segments
    /// with the target file. A wrong candidate is rejected in milliseconds instead of a full rebuild.</summary>
    public static async Task<bool> SpotCheckAsync(Recipe r, string targetPath, CancellationToken ct = default)
    {
        await using var produced = new RecipeRangeSource(r);
        await using var actual = new FileRangeSource(targetPath);
        var step = Math.Max(1, r.Segments.Count / 12);
        var a = new byte[1 << 20];
        var b = new byte[1 << 20];
        foreach (var seg in r.Segments.Where((_, i) => i % step == 0).Take(12))
        {
            var len = (int)Math.Min(a.Length, seg.Length);
            var n1 = await produced.ReadFullyAsync(seg.Offset, a.AsMemory(0, len), ct);
            var n2 = await actual.ReadFullyAsync(seg.Offset, b.AsMemory(0, len), ct);
            if (n1 != n2 || !a.AsSpan(0, n1).SequenceEqual(b.AsSpan(0, n2)))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Rebuilds the whole content and checks it hashes to the target sha (the only proof that counts).</summary>
    public static async Task<bool> VerifyAsync(Recipe r, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        await using var src = new RecipeRangeSource(r);
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[8 << 20];
        long pos = 0;
        while (pos < r.TargetSize)
        {
            var n = await src.ReadFullyAsync(pos, buf, ct);
            if (n == 0)
            {
                return false;
            }
            h.AppendData(buf, 0, n);
            pos += n;
            progress?.Report(pos);
        }
        return Convert.ToHexStringLower(h.GetHashAndReset()) == r.TargetSha;
    }
}

/// <summary>Random access over the bytes a recipe produces.</summary>
public sealed class RecipeRangeSource : IRangeSource
{
    private readonly Recipe _r;
    private readonly byte[] _header;
    private readonly FileRangeSource?[] _files;

    /// <summary>Creates the reader; source files are opened lazily.</summary>
    public RecipeRangeSource(Recipe r)
    {
        _r = r;
        _header = Convert.FromBase64String(r.HeaderBase64);
        _files = new FileRangeSource?[r.Sources.Count];
    }

    /// <inheritdoc/>
    public long Length => _r.TargetSize;

    /// <inheritdoc/>
    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken ct)
    {
        if (offset >= _r.TargetSize || buffer.Length == 0)
        {
            return 0;
        }
        if (offset < _header.Length)
        {
            var n = (int)Math.Min(buffer.Length, _header.Length - offset);
            _header.AsMemory((int)offset, n).CopyTo(buffer);
            return n;
        }
        var seg = FindSegment(offset);
        var within = offset - seg.Offset;
        var want = (int)Math.Min(buffer.Length, seg.Length - within);
        var file = _files[seg.Source] ??= new FileRangeSource(_r.Sources[seg.Source].Path);
        if (seg.Op == RecipeOp.Copy)
        {
            return await file.ReadFullyAsync(seg.SourceOffset + within, buffer[..want], ct);
        }
        // Cast: target element k (2 bytes) comes from source element k (4 bytes).
        var firstElem = within / 2;
        var lastElem = (within + want - 1) / 2;
        var count = (int)(lastElem - firstElem + 1);
        var f32 = System.Buffers.ArrayPool<byte>.Shared.Rent(count * 4);
        var bf = System.Buffers.ArrayPool<byte>.Shared.Rent(count * 2);
        try
        {
            var got = await file.ReadFullyAsync(seg.SourceOffset + firstElem * 4, f32.AsMemory(0, count * 4), ct);
            if (got != count * 4)
            {
                throw new IOException(Loc.T("recipe.short_source"));
            }
            Bf16.FromF32(f32.AsSpan(0, count * 4), bf.AsSpan(0, count * 2));
            bf.AsSpan((int)(within - firstElem * 2), want).CopyTo(buffer.Span);
            return want;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(f32);
            System.Buffers.ArrayPool<byte>.Shared.Return(bf);
        }
    }

    private RecipeSegment FindSegment(long offset)
    {
        int lo = 0, hi = _r.Segments.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var s = _r.Segments[mid];
            if (offset < s.Offset)
            {
                hi = mid - 1;
            }
            else if (offset >= s.Offset + s.Length)
            {
                lo = mid + 1;
            }
            else
            {
                return s;
            }
        }
        throw new InvalidDataException(Loc.T("recipe.no_segment"));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var f in _files)
        {
            if (f is not null)
            {
                await f.DisposeAsync();
            }
        }
    }
}
