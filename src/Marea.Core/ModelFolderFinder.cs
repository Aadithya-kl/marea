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

    /// <summary>Default model folders of LM Studio, Jan, GPT4All, Ollama and the Hugging Face cache.</summary>
    public static IEnumerable<string> KnownAppFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(home, ".lmstudio", "models");
        yield return Path.Combine(home, ".cache", "lm-studio", "models");
        yield return Path.Combine(roaming, "Jan", "data", "llamacpp", "models");
        yield return Path.Combine(home, "jan", "models");
        yield return Path.Combine(local, "nomic.ai", "GPT4All");
        // Ollama keeps weights as content-addressed blobs without extensions.
        yield return Path.Combine(OllamaRegistry.DefaultModelsDir, "blobs");
        yield return Environment.GetEnvironmentVariable("HF_HUB_CACHE") is { Length: > 0 } hub
            ? hub
            : Path.Combine(Environment.GetEnvironmentVariable("HF_HOME") is { Length: > 0 } hf ? hf : Path.Combine(home, ".cache", "huggingface"), "hub");
    }

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
        CancellationToken ct = default, IEnumerable<string>? knownFolders = null, long minFileBytes = 50L << 20)
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
                var (bytes, count) = Weights(d, minFileBytes);
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
        // Model folders of popular apps, also where the generic walk does not look (AppData, hidden dirs).
        foreach (var known in (knownFolders ?? KnownAppFolders()).Where(Directory.Exists))
        {
            if (found.Any(f => FileSystemFacts.IsUnder(known, f.Path) || string.Equals(known, f.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var (bytes, count) = Weights(new DirectoryInfo(known), minFileBytes);
            if (bytes >= minBytes && FileSystemFacts.RejectRoot(known, []) is null)
            {
                found.RemoveAll(f => FileSystemFacts.IsUnder(f.Path, known));
                found.Add(new FoundFolder(known, bytes, count));
            }
        }
        return found.OrderByDescending(f => f.ModelBytes).ToList();
    }

    private static (long, int) Weights(DirectoryInfo d, long minFile)
    {
        long bytes = 0;
        var n = 0;
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var f in d.EnumerateFiles("*", opts))
        {
            // Hugging Face cache blobs and similar content-addressed stores have no extension.
            var weight = WeightExt.Contains(f.Extension) || (f.Extension.Length == 0 && f.Directory?.Name == "blobs");
            if (weight && f.Length >= minFile)
            {
                bytes += f.Length;
                n++;
            }
        }
        return (bytes, n);
    }
}
