namespace Marea.Core;

/// <summary>Health of one public source (a pinned Hugging Face file or an Ollama blob).</summary>
public enum SourceHealth
{
    /// <summary>Never checked by the watch.</summary>
    Unknown = 0,
    /// <summary>Serves the exact bytes, and upstream still publishes them.</summary>
    Healthy = 1,
    /// <summary>Could not be checked (network, server error): nothing is concluded.</summary>
    Unreachable = 2,
    /// <summary>Still serves the bytes, but upstream moved away from them (removed from the latest
    /// revision, repository gated or disabled, tag deleted): it may disappear any day.</summary>
    AtRisk = 3,
    /// <summary>No longer serves the bytes to anonymous clients (deleted, private, gated, taken down).</summary>
    Gone = 4,
}

/// <summary>Outcome of one HTTP probe.</summary>
public enum ProbeResult
{
    /// <summary>Served.</summary>
    Ok,
    /// <summary>404, 410 or 451: not there any more.</summary>
    Missing,
    /// <summary>401 or 403: needs an account now (private or gated).</summary>
    Restricted,
    /// <summary>Network or server error: no conclusion.</summary>
    Error,
}

/// <summary>What the Hub says about a repository right now.</summary>
/// <param name="Result">Outcome of the repository API call.</param>
/// <param name="Gated">Downloads need an account and an agreement.</param>
/// <param name="Disabled">Disabled by the Hub (for instance after a report).</param>
/// <param name="LatestShas">sha256 of every LFS file in the latest revision.</param>
/// <param name="ScansDone">True when the Hub's security scanners have scanned the repository.</param>
/// <param name="Flagged">Files the Hub's scanners flagged, by path, with their level (unsafe, caution...).</param>
public sealed record RepoView(ProbeResult Result, bool Gated, bool Disabled, IReadOnlySet<string> LatestShas,
    bool ScansDone = false, IReadOnlyDictionary<string, string>? Flagged = null);

/// <summary>What the registry says about one local Ollama tag.</summary>
/// <param name="Name">Tag as shown to the user (llama3.2:3b).</param>
/// <param name="Result">Outcome of the manifest request.</param>
/// <param name="StillPointsHere">True when the published manifest still lists this blob.</param>
public sealed record TagView(string Name, ProbeResult Result, bool StillPointsHere);

/// <summary>Stored health of a source.</summary>
/// <param name="Locator">Source locator (same as in the sources table).</param>
/// <param name="State">Current health.</param>
/// <param name="Detail">Coded explanation (render with Loc.Render).</param>
/// <param name="CheckedUtc">Last check.</param>
/// <param name="SinceUtc">When the current state began.</param>
public sealed record SourceStatus(string Locator, SourceHealth State, string Detail, DateTime CheckedUtc, DateTime SinceUtc);

/// <summary>Pure rules that turn probe results into a health state. No I/O.</summary>
public static class HealthRules
{
    /// <summary>Health of a pinned Hugging Face file.</summary>
    public static (SourceHealth, string) ForHub(ProbeResult file, RepoView repo, string sha256) => file switch
    {
        ProbeResult.Missing => (SourceHealth.Gone, Loc.Code("watch.file_gone")),
        ProbeResult.Restricted => (SourceHealth.Gone, Loc.Code("watch.file_restricted")),
        ProbeResult.Error => (SourceHealth.Unreachable, Loc.Code("watch.unreachable")),
        _ when repo.Disabled => (SourceHealth.AtRisk, Loc.Code("watch.repo_disabled")),
        _ when repo.Gated => (SourceHealth.AtRisk, Loc.Code("watch.repo_gated")),
        _ when repo.Result is ProbeResult.Missing or ProbeResult.Restricted => (SourceHealth.AtRisk, Loc.Code("watch.repo_hidden")),
        _ when repo.Result == ProbeResult.Ok && !repo.LatestShas.Contains(sha256) => (SourceHealth.AtRisk, Loc.Code("watch.removed_upstream")),
        _ => (SourceHealth.Healthy, ""),
    };

    /// <summary>Health of an Ollama blob, given what the registry says about the local tags using it.</summary>
    public static (SourceHealth, string) ForOllama(ProbeResult blob, IReadOnlyList<TagView> tags) => blob switch
    {
        ProbeResult.Missing or ProbeResult.Restricted => (SourceHealth.Gone, Loc.Code("watch.blob_gone")),
        ProbeResult.Error => (SourceHealth.Unreachable, Loc.Code("watch.unreachable")),
        _ when tags.Count > 0 && tags.All(t => t.Result == ProbeResult.Missing) =>
            (SourceHealth.AtRisk, Loc.Code("watch.tag_removed", string.Join(", ", tags.Select(t => t.Name)))),
        _ when tags.Any(t => t.Result == ProbeResult.Ok && !t.StillPointsHere) =>
            (SourceHealth.Healthy, Loc.Code("watch.tag_updated")),
        _ => (SourceHealth.Healthy, ""),
    };

    /// <summary>True when the source can still be counted on to bring a file back.</summary>
    public static bool Usable(SourceHealth h) => h is SourceHealth.Unknown or SourceHealth.Healthy or SourceHealth.Unreachable;
}
