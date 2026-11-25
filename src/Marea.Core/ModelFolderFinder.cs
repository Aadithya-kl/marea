namespace Marea.Core;

/// <summary>A folder that looks like it holds AI model weights.</summary>
/// <param name="Path">Folder path.</param>
/// <param name="ModelBytes">Bytes of model-weight files inside.</param>
/// <param name="ModelFiles">Number of model-weight files.</param>
public sealed record FoundFolder(string Path, long ModelBytes, int ModelFiles);

/// <summary>Finds model folders under the user profile so the first run needs no typing.
/// Read-only: it only lists directories and file sizes.</summary>
public static class ModelFolderFinder
{
    private static readonly HashSet<string> WeightExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".safetensors", ".gguf", ".ckpt", ".pt", ".pth", ".bin", ".onnx", ".sft",
    };

    private static readonly HashSet<string> Interesting = new(StringComparer.OrdinalIgnoreCase)
    {
        "checkpoints", "models", "hub", "llm", "loras", "weights",
    };

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".venv", "venv", "site-packages", "Windows", "AppData", ".nuget", "Lib", "__pycache__",
    };

    /// <summary>Folders named like model stores (checkpoints, models, hub, ...) holding at least
    /// <paramref name="minBytes"/> of weights, outermost first, never nested in one another.</summary>
    public static List<FoundFolder> Find(IEnumerable<string> searchRoots, long minBytes = 1L << 30, int maxDepth = 5,
        CancellationToken ct = default)
    {
        var found = new List<FoundFolder>();
        foreach (var root in searchRoots.Where(Directory.Exists))
        {
            Visit(new DirectoryInfo(root), 0);
        }
        void Visit(DirectoryInfo d, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > maxDepth || Skip.Contains(d.Name) || d.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return;
            }
            if (Interesting.Contains(d.Name))
            {
                var (bytes, count) = Weights(d);
                if (bytes >= minBytes && FileSystemFacts.RejectRoot(d.FullName, []) is null)
                {
                    found.Add(new FoundFolder(d.FullName, bytes, count));
                    return; // do not propose nested folders
                }
            }
            IEnumerable<DirectoryInfo> subs;
            try
            {
                subs = d.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true }).ToList();
            }
            catch (IOException)
            {
                return;
            }
            foreach (var s in subs)
            {
                Visit(s, depth + 1);
            }
        }
        return found.OrderByDescending(f => f.ModelBytes).ToList();
    }

    private static (long, int) Weights(DirectoryInfo d)
    {
        long bytes = 0;
        var n = 0;
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var f in d.EnumerateFiles("*", opts))
        {
            if (WeightExt.Contains(f.Extension) && f.Length >= 50L << 20)
            {
                bytes += f.Length;
                n++;
            }
        }
        return (bytes, n);
    }
}
