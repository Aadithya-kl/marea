using System.Text.Json;

namespace Marea.Core;

/// <summary>Maps Ollama model blobs to the names people type (<c>llama3.2:3b</c>), from local manifests.</summary>
public static class OllamaNames
{
    /// <summary>Model-layer digest (sha256 hex) → model names, for the manifests under <paramref name="modelsDir"/>.</summary>
    public static Dictionary<string, List<string>> Read(string modelsDir)
    {
        var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var root = Path.Combine(modelsDir, "manifests");
        if (!Directory.Exists(root))
        {
            return names;
        }
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(root, "*", opts))
        {
            var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
            if (parts.Length < 4)
            {
                continue;
            }
            var name = Display(parts);
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("layers", out var layers))
                {
                    continue;
                }
                foreach (var layer in layers.EnumerateArray())
                {
                    var media = layer.TryGetProperty("mediaType", out var m) ? m.GetString() : null;
                    var digest = layer.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
                    if (media == "application/vnd.ollama.image.model" && digest.StartsWith("sha256:", StringComparison.Ordinal))
                    {
                        var hex = digest[7..];
                        if (!names.TryGetValue(hex, out var list))
                        {
                            names[hex] = list = [];
                        }
                        list.Add(name);
                    }
                }
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // An unreadable manifest only means one name fewer.
            }
        }
        foreach (var list in names.Values)
        {
            list.Sort(StringComparer.OrdinalIgnoreCase);
        }
        return names;
    }

    /// <summary>host/namespace/model/tag → the short name the Ollama CLI shows.</summary>
    internal static string Display(string[] parts)
    {
        var tag = parts[^1];
        var model = parts[^2];
        var ns = string.Join('/', parts[1..^2]);
        return parts[0] == "registry.ollama.ai"
            ? ns == "library" ? $"{model}:{tag}" : $"{ns}/{model}:{tag}"
            : $"{parts[0]}/{ns}/{model}:{tag}";
    }
}
