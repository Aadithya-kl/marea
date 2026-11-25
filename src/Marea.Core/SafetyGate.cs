namespace Marea.Core;

/// <summary>Why a file has to stay on disk.</summary>
public enum BlockerCode
{
    /// <summary>Not on disk right now.</summary>
    NotLocal,
    /// <summary>Outside the managed folders.</summary>
    OutsideRoots,
    /// <summary>Used or modified too recently.</summary>
    RecentlyUsed,
    /// <summary>Needed to rebuild an evaporated file.</summary>
    Pinned,
    /// <summary>No verified source at all.</summary>
    Unique,
    /// <summary>Its only source would leave with it.</summary>
    SourceLeaving,
}

/// <summary>A reason to keep a file.</summary>
/// <param name="Code">Machine-readable reason.</param>
/// <param name="Key">Translation key.</param>
/// <param name="Args">Translation arguments.</param>
public sealed record Blocker(BlockerCode Code, string Key, params object?[] Args)
{
    /// <summary>Explanation in the current language.</summary>
    public string Text => Loc.T(Key, Args);

    /// <summary>Storable form (see <see cref="Loc.Code"/>).</summary>
    public string Coded => Loc.Code(Key, Args);
}

/// <summary>How an evaporated file would come back.</summary>
public enum ReturnPath
{
    /// <summary>No independent way back.</summary>
    None,
    /// <summary>Copied from an identical local file (seconds, offline).</summary>
    LocalCopy,
    /// <summary>Recomputed from other local files (seconds, offline).</summary>
    Recipe,
    /// <summary>Copied from the mirror / NAS (needs the LAN).</summary>
    Mirror,
    /// <summary>Downloaded from a public hub (needs internet).</summary>
    Download,
}

/// <summary>Verdict for one file: either it may evaporate or why not.</summary>
/// <param name="File">The file.</param>
/// <param name="Blockers">Reasons it must stay; empty when it may evaporate.</param>
/// <param name="RebuildSeconds">Estimated seconds to bring it back.</param>
/// <param name="Path">Cheapest way back.</param>
public sealed record GateVerdict(FileRecord File, IReadOnlyList<Blocker> Blockers, double RebuildSeconds, ReturnPath Path)
{
    /// <summary>True when nothing blocks evaporation.</summary>
    public bool Allowed => Blockers.Count == 0;

    /// <summary>True when the file comes back offline in seconds (local copy or recipe).</summary>
    public bool Instant => Path is ReturnPath.LocalCopy or ReturnPath.Recipe;
}

/// <summary>The rules that make evaporation safe. A file may leave the disk only when a verified,
/// independent way to get the exact bytes back will still exist afterwards.</summary>
public sealed class SafetyGate(Ledger ledger, MareaSettings settings)
{
    /// <summary>Evaluates every local file, aware of what is already evaporated.</summary>
    public List<GateVerdict> EvaluateAll(DateTime nowUtc)
    {
        var all = ledger.Files();
        var pinned = PinnedPaths(all);
        return all.Where(f => f.Residency == Residency.Local)
                  .Select(f => Evaluate(f, all, pinned, [], nowUtc))
                  .ToList();
    }

    /// <summary>Evaluates one file as if <paramref name="alsoLeaving"/> were evaporating together with it.</summary>
    public GateVerdict Evaluate(FileRecord f, List<FileRecord> all, HashSet<string> pinned,
        HashSet<string> alsoLeaving, DateTime nowUtc)
    {
        var why = new List<Blocker>();
        if (f.Residency != Residency.Local)
        {
            why.Add(new(BlockerCode.NotLocal, "gate.not_local"));
        }
        if (!settings.ManagedRoots.Any(r => FileSystemFacts.IsUnder(f.Path, r)))
        {
            why.Add(new(BlockerCode.OutsideRoots, "gate.outside_roots"));
        }
        var idle = (nowUtc - Latest(f.LastAccessUtc, new DateTime(f.MTimeTicks, DateTimeKind.Utc))).TotalDays;
        if (idle < settings.MinIdleDays)
        {
            why.Add(new(BlockerCode.RecentlyUsed, "gate.recently_used", Math.Floor(idle), settings.MinIdleDays));
        }
        if (pinned.Contains(f.Path))
        {
            why.Add(new(BlockerCode.Pinned, "gate.pinned"));
        }
        var (seconds, path) = RebuildSeconds(f, all, alsoLeaving, why);
        return new GateVerdict(f, why, seconds, path);
    }

    /// <summary>Cheapest independent way back; adds a blocker when there is none.</summary>
    private (double, ReturnPath) RebuildSeconds(FileRecord f, List<FileRecord> all, HashSet<string> leaving, List<Blocker> why)
    {
        var gb = f.Size / 1e9;
        var otherCopy = all.Any(o => o.Sha256 == f.Sha256 && o.Residency == Residency.Local
                                    && !string.Equals(o.Path, f.Path, StringComparison.OrdinalIgnoreCase)
                                    && !leaving.Contains(o.Path));
        if (otherCopy)
        {
            return (gb / 1.5, ReturnPath.LocalCopy);
        }
        var sources = ledger.SourcesOf(f.Sha256);
        if (sources.Any(s => s.Kind == SourceKind.Recipe) && ledger.GetRecipe(f.Sha256) is { } json)
        {
            var r = Recipe.FromJson(json);
            var sourcesHere = r.Sources.All(s => !leaving.Contains(s.Path)
                && all.Any(o => string.Equals(o.Path, s.Path, StringComparison.OrdinalIgnoreCase) && o.Residency == Residency.Local));
            if (r.SourcesUnchanged() && sourcesHere)
            {
                return (gb / 0.4, ReturnPath.Recipe);
            }
        }
        if (sources.Any(s => s.Kind == SourceKind.Mirror))
        {
            return (gb / 0.1, ReturnPath.Mirror);
        }
        if (sources.Any(s => s.Kind == SourceKind.HuggingFace))
        {
            return (gb * 1000 / Math.Max(1, settings.BandwidthMBps), ReturnPath.Download);
        }
        why.Add(f.Class == Recoverability.Unique
            ? new(BlockerCode.Unique, "gate.unique")
            : new(BlockerCode.SourceLeaving, "gate.source_leaving"));
        return (double.PositiveInfinity, ReturnPath.None);
    }

    /// <summary>Local files that an evaporated file needs to be rebuilt (recipe sources, last local copy).</summary>
    public HashSet<string> PinnedPaths(List<FileRecord> all)
    {
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in all.Where(f => f.Residency == Residency.Evaporated))
        {
            var external = ledger.SourcesOf(gone.Sha256).Any(s => s.Kind is SourceKind.HuggingFace or SourceKind.Mirror);
            if (external)
            {
                continue;
            }
            if (ledger.GetRecipe(gone.Sha256) is { } json)
            {
                foreach (var s in Recipe.FromJson(json).Sources)
                {
                    pinned.Add(s.Path);
                }
            }
            foreach (var copy in all.Where(o => o.Sha256 == gone.Sha256 && o.Residency == Residency.Local))
            {
                pinned.Add(copy.Path);
            }
        }
        return pinned;
    }

    private static DateTime Latest(DateTime a, DateTime b) => a > b ? a : b;
}
