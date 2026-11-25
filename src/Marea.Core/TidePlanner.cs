namespace Marea.Core;

/// <summary>A file chosen to evaporate and what it would cost to bring back.</summary>
/// <param name="File">The file.</param>
/// <param name="RebuildSeconds">Estimated seconds to bring it back.</param>
/// <param name="ExpectedCost">Probability of use × rebuild seconds, per GB freed (lower is better).</param>
/// <param name="Path">How it would come back.</param>
public sealed record PlanItem(FileRecord File, double RebuildSeconds, double ExpectedCost, ReturnPath Path)
{
    /// <summary>True when it comes back offline in seconds.</summary>
    public bool Instant => Path is ReturnPath.LocalCopy or ReturnPath.Recipe;
}

/// <summary>Chooses what leaves the disk: the files whose expected cost of coming back, per GB freed,
/// is lowest. Probability of use decays with idle time (half-life ~ 10 days).</summary>
public sealed class TidePlanner(SafetyGate gate, Ledger ledger)
{
    /// <summary>Probability that a file idle for <paramref name="idleDays"/> is needed again soon.</summary>
    public static double UseProbability(double idleDays) => Math.Exp(-Math.Max(0, idleDays) / 14.0);

    /// <summary>Greedy plan freeing at least <paramref name="bytesNeeded"/> (or everything allowed when 0 or less).
    /// Every pick is re-checked against the files already picked, so two copies never leave together
    /// unless an external source exists.</summary>
    public List<PlanItem> Plan(long bytesNeeded, DateTime nowUtc, bool instantOnly = false)
    {
        var all = ledger.Files();
        var pinned = gate.PinnedPaths(all);
        var leaving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = gate.EvaluateAll(nowUtc).Where(v => v.Allowed && (!instantOnly || v.Instant))
            .Select(v => new PlanItem(v.File, v.RebuildSeconds, Cost(v, nowUtc), v.Path))
            .OrderBy(p => p.ExpectedCost).ThenByDescending(p => p.File.Size)
            .ToList();
        var plan = new List<PlanItem>();
        long freed = 0;
        foreach (var item in ranked)
        {
            if (bytesNeeded > 0 && freed >= bytesNeeded)
            {
                break;
            }
            var recheck = gate.Evaluate(item.File, all, pinned, leaving, nowUtc);
            if (!recheck.Allowed || (instantOnly && !recheck.Instant))
            {
                continue;
            }
            plan.Add(item with { RebuildSeconds = recheck.RebuildSeconds, Path = recheck.Path });
            leaving.Add(item.File.Path);
            freed += item.File.Size;
            // A file rebuilt by a recipe needs its source files on disk: they stay for the rest of the plan.
            if (recheck.Path == ReturnPath.Recipe && ledger.GetRecipe(item.File.Sha256) is { } json)
            {
                foreach (var s in Recipe.FromJson(json).Sources)
                {
                    pinned.Add(s.Path);
                }
            }
        }
        return plan;
    }

    private static double Cost(GateVerdict v, DateTime nowUtc)
    {
        var lastUse = v.File.LastAccessUtc > new DateTime(v.File.MTimeTicks, DateTimeKind.Utc)
            ? v.File.LastAccessUtc : new DateTime(v.File.MTimeTicks, DateTimeKind.Utc);
        var p = UseProbability((nowUtc - lastUse).TotalDays);
        return p * v.RebuildSeconds / Math.Max(v.File.Size / 1e9, 1e-3);
    }
}
