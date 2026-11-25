namespace Marea.Core;

/// <summary>A file chosen to evaporate and what it would cost to bring back.</summary>
/// <param name="File">The file.</param>
/// <param name="RebuildSeconds">Estimated seconds to bring it back.</param>
/// <param name="ExpectedCost">Probability of use × rebuild seconds, per GB freed (lower is better).</param>
public sealed record PlanItem(FileRecord File, double RebuildSeconds, double ExpectedCost);

/// <summary>Chooses what leaves the disk: the files whose expected cost of coming back, per GB freed,
/// is lowest. Probability of use decays with idle time (half-life ~ 10 days).</summary>
public sealed class TidePlanner(SafetyGate gate, Ledger ledger)
{
    /// <summary>Probability that a file idle for <paramref name="idleDays"/> is needed again soon.</summary>
    public static double UseProbability(double idleDays) => Math.Exp(-Math.Max(0, idleDays) / 14.0);

    /// <summary>Greedy plan freeing at least <paramref name="bytesNeeded"/> (or everything allowed when 0 or less).
    /// Every pick is re-checked against the files already picked, so two copies never leave together
    /// unless an external source exists.</summary>
    public List<PlanItem> Plan(long bytesNeeded, DateTime nowUtc)
    {
        var all = ledger.Files();
        var pinned = gate.PinnedPaths(all);
        var leaving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = gate.EvaluateAll(nowUtc).Where(v => v.Allowed)
            .Select(v => new PlanItem(v.File, v.RebuildSeconds, Cost(v, nowUtc)))
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
            if (!recheck.Allowed)
            {
                continue;
            }
            plan.Add(item with { RebuildSeconds = recheck.RebuildSeconds });
            leaving.Add(item.File.Path);
            freed += item.File.Size;
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
