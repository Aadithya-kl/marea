using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marea.Core;

/// <summary>Finds which public Hugging Face repositories the local models probably came from,
/// using only evidence already on disk. Every candidate is later proven (or not) by SHA-256.</summary>
public static partial class RepoDiscovery
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-]*/[A-Za-z0-9][A-Za-z0-9_.\-]*$")]
    private static partial Regex RepoId();

    [GeneratedRegex(@"huggingface\.co/(?:models/)?([A-Za-z0-9][\w.\-]*/[A-Za-z0-9][\w.\-]*?)(?:\.git)?\s*$", RegexOptions.Multiline)]
    private static partial Regex GitRemote();

    [GeneratedRegex(@"^base_model:[ \t]*(.*?)\r?$((?:\r?\n[ \t]*-[ \t]*.+)*)", RegexOptions.Multiline)]
    private static partial Regex BaseModel();

    /// <summary>True for a syntactically valid owner/name repository id.</summary>
    public static bool IsRepoId(string s) => RepoId().IsMatch(s) && !s.Contains("..", StringComparison.Ordinal);

    /// <summary>Repository ids found in the HF cache, in metadata files under <paramref name="roots"/>,
    /// and in Ollama manifests pulled from hf.co.</summary>
    public static HashSet<string> LocalEvidence(IEnumerable<string> roots, string? ollamaModelsDir = null)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cache in HubCacheDirs().Where(Directory.Exists))
        {
            foreach (var d in Directory.EnumerateDirectories(cache, "models--*"))
            {
                var parts = Path.GetFileName(d)["models--".Length..].Split("--");
                if (parts.Length == 2)
                {
                    Add(found, $"{parts[0]}/{parts[1]}");
                }
            }
        }
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 5 };
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", opts))
            {
                var name = Path.GetFileName(file);
                try
                {
                    if (name.Equals("README.md", StringComparison.OrdinalIgnoreCase) && new FileInfo(file).Length < 1 << 20)
                    {
                        foreach (Match m in BaseModel().Matches(File.ReadAllText(file)))
                        {
                            Add(found, m.Groups[1].Value);
                            foreach (var line in m.Groups[2].Value.Split('\n'))
                            {
                                Add(found, line.Trim().TrimStart('-'));
                            }
                        }
                    }
                    else if ((name.Equals("config.json", StringComparison.OrdinalIgnoreCase)
                              || name.Equals("model_index.json", StringComparison.OrdinalIgnoreCase)) && new FileInfo(file).Length < 1 << 20)
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(file));
                        if (doc.RootElement.ValueKind == JsonValueKind.Object
                            && doc.RootElement.TryGetProperty("_name_or_path", out var p) && p.ValueKind == JsonValueKind.String)
                        {
                            Add(found, p.GetString()!);
                        }
                    }
                    else if (name.Equals("config", StringComparison.Ordinal) && Path.GetFileName(Path.GetDirectoryName(file)) == ".git")
                    {
                        foreach (Match m in GitRemote().Matches(File.ReadAllText(file)))
                        {
                            Add(found, m.Groups[1].Value);
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
                {
                    // Unreadable metadata is simply not evidence.
                }
            }
        }
        if (ollamaModelsDir is not null)
        {
            foreach (var blob in OllamaRegistry.ReadManifests(ollamaModelsDir).Where(b => b.Host == "hf.co"))
            {
                Add(found, blob.Repository);
            }
        }
        return found;
    }

    /// <summary>Names of local folders that hold model weights (used to filter large authors' repo lists).</summary>
    public static HashSet<string> LocalModelNames(IEnumerable<FileRecord> files) =>
        files.Select(f => Path.GetFileName(Path.GetDirectoryName(f.Path)) ?? "")
             .Concat(files.Select(f => Path.GetFileNameWithoutExtension(f.Path)))
             .Where(n => n.Length >= 4)
             .Select(n => n.ToLowerInvariant())
             .ToHashSet();

    /// <summary>True when a repository name plausibly matches one of the local model names.</summary>
    public static bool LooksRelated(string repoId, HashSet<string> localNames)
    {
        var name = repoId[(repoId.IndexOf('/') + 1)..].ToLowerInvariant();
        return localNames.Any(l => name.Contains(l, StringComparison.Ordinal) || l.Contains(name, StringComparison.Ordinal));
    }

    private static IEnumerable<string> HubCacheDirs()
    {
        if (Environment.GetEnvironmentVariable("HF_HUB_CACHE") is { Length: > 0 } hub)
        {
            yield return hub;
        }
        if (Environment.GetEnvironmentVariable("HF_HOME") is { Length: > 0 } home)
        {
            yield return Path.Combine(home, "hub");
        }
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "huggingface", "hub");
    }

    private static void Add(HashSet<string> set, string candidate)
    {
        var c = candidate.Trim().TrimStart('-').Trim().Trim('"', '\'');
        if (IsRepoId(c))
        {
            set.Add(c);
        }
    }
}
