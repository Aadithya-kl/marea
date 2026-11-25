using System.Buffers.Binary;
using System.IO.Compression;

namespace Marea.Core;

/// <summary>Identifies a model file by its bytes (not its extension) and checks whether loading it
/// could run code. Reads headers only; never loads, unpickles or executes anything.</summary>
public static class ModelInspector
{
    private const long MaxPickleEntry = 256L << 20;

    /// <summary>Inspects one file; unreadable or unknown files come back as <see cref="ModelMeta.Data"/>.</summary>
    public static ModelMeta Inspect(string path)
    {
        try
        {
            Span<byte> head = stackalloc byte[8];
            int got;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                got = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            }
            head = head[..got];
            if (got >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(head) == GgufReader.Magic)
            {
                return Gguf(path);
            }
            if (got >= 4 && head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4)
            {
                return Zip(path);
            }
            if (got >= 2 && head[0] == 0x80 && head[1] is >= 2 and <= 5)
            {
                return RawPickle(path);
            }
            if (SafetensorsFile.TryRead(path) is { } st)
            {
                return SafetensorsMeta(st);
            }
            if (path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                return new ModelMeta("ONNX", null, null, null, 0, SafetyVerdict.SafeFormat, []);
            }
            return ModelMeta.Data;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ModelMeta.Data;
        }
    }

    private static ModelMeta Gguf(string path)
    {
        var g = GgufReader.TryRead(path);
        if (g is null)
        {
            return new ModelMeta("GGUF", null, null, null, 0, SafetyVerdict.Suspicious, ["unreadable GGUF header"]);
        }
        var red = ImportRating.TemplateFindings(g.ChatTemplate);
        return new ModelMeta("GGUF", g.Architecture, g.Name, g.FileType, g.Parameters,
            red.Count > 0 ? SafetyVerdict.Suspicious : SafetyVerdict.SafeFormat,
            red.Select(r => "chat template: " + r).ToList());
    }

    private static ModelMeta SafetensorsMeta(SafetensorsFile st)
    {
        var dominant = st.Tensors.GroupBy(t => t.Dtype).OrderByDescending(g => g.Sum(t => t.Bytes)).FirstOrDefault()?.Key;
        return new ModelMeta("safetensors", null, null, dominant, st.Tensors.Sum(t => t.Elements), SafetyVerdict.SafeFormat, []);
    }

    /// <summary>PyTorch zip checkpoints (.pt, .pth, .bin, .ckpt) and TorchScript archives.</summary>
    private static ModelMeta Zip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var pickles = zip.Entries.Where(e => e.FullName.EndsWith(".pkl", StringComparison.OrdinalIgnoreCase)).ToList();
        var code = zip.Entries.Any(e => e.FullName.Contains("/code/", StringComparison.Ordinal)
                                        && e.FullName.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
        if (pickles.Count == 0)
        {
            return ModelMeta.Data;
        }
        var imports = new List<(string, string)>();
        var complete = true;
        foreach (var e in pickles.Where(e => e.Length <= MaxPickleEntry))
        {
            using var s = new BufferedStream(e.Open(), 1 << 16);
            var rep = PickleScanner.Scan(s);
            imports.AddRange(rep.Imports);
            complete &= rep.Complete;
        }
        complete &= pickles.All(e => e.Length <= MaxPickleEntry);
        return PickleMeta(code ? "TorchScript" : "PyTorch", imports, complete, code);
    }

    private static ModelMeta RawPickle(string path)
    {
        using var s = new BufferedStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), 1 << 16);
        var rep = PickleScanner.Scan(s);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext is ".pt" or ".pth" or ".ckpt" or ".bin" ? "PyTorch" : "pickle";
        return PickleMeta(format, rep.Imports, rep.Complete, false);
    }

    private static ModelMeta PickleMeta(string format, IReadOnlyList<(string Module, string Name)> imports, bool complete, bool code)
    {
        var verdict = ImportRating.Rate(imports);
        var findings = imports.Where(i => ImportRating.Rate(i.Module, i.Name) >= SafetyVerdict.Suspicious)
            .Select(i => $"{i.Module}.{i.Name}").Distinct().Take(12).ToList();
        if (code)
        {
            findings.Insert(0, "contains Python code (TorchScript)");
            verdict = verdict < SafetyVerdict.Suspicious ? SafetyVerdict.Suspicious : verdict;
        }
        if (!complete)
        {
            findings.Add("could not be read to the end");
            verdict = verdict < SafetyVerdict.Suspicious ? SafetyVerdict.Suspicious : verdict;
        }
        return new ModelMeta(format, null, null, null, 0, verdict, findings);
    }
}
