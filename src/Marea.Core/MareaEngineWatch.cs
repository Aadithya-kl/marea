namespace Marea.Core;

/// <summary>Result of a watch run after Marea acted on it.</summary>
/// <param name="Report">What the probes found.</param>
/// <param name="Rescued">Evaporated files brought back while their source still answered.</param>
/// <param name="Protected">Files copied to the mirror because they may be the last copy.</param>
/// <param name="Remaining">Findings still open after acting.</param>
public sealed record WatchOutcome(WatchReport Report, int Rescued, int Protected, List<WatchFinding> Remaining);

/// <summary>Source watch: early warnings when public sources start to disappear, and rescue.</summary>
public sealed partial class MareaEngine
{
    /// <summary>Checks every public source and, when <paramref name="act"/> and the settings allow it,
    /// brings back evaporated files whose source is disappearing and copies possible last copies to
    /// the mirror. Bringing files back needs the provider role.</summary>
    public async Task<WatchOutcome> WatchAsync(bool act, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var probe = new PublicSourceProbe(_hub, _ollama, OllamaRegistry.DefaultModelsDir);
        var report = await new SourceWatch(Ledger, probe).CheckAsync(progress, ct);
        var rescued = 0;
        var protectedCount = 0;
        if (act && Settings.RescueAtRisk)
        {
            foreach (var f in report.Findings.Where(x => x.Action == WatchAction.Rescue).Select(x => x.File))
            {
                var why = await BringBackAsync([f], progress, ct);
                Ledger.Log(why is null ? "j.rescued" : "j.rescue_failed", f.Path, f.Size, why is null, why ?? "");
                rescued += why is null ? 1 : 0;
            }
            var toProtect = report.Findings.Where(x => x.Action == WatchAction.Protect).Select(x => x.File).ToList();
            if (toProtect.Count > 0 && Settings.MirrorRoot is { Length: > 0 } m && Directory.Exists(m))
            {
                protectedCount = await ReplicateAsync(toProtect, progress, ct);
            }
        }
        var resolver = new SourceResolver(Ledger, Settings, _hub);
        foreach (var f in Ledger.Files())
        {
            (f.Class, f.SourceLabel) = resolver.Classify(f);
            Ledger.Upsert(f);
        }
        Settings.LastWatchUtc = DateTime.UtcNow;
        SaveSettings();
        return new WatchOutcome(report, rescued, protectedCount, SourceWatch.Assess(Ledger));
    }

    /// <summary>Files that currently need attention because of their public sources (no network).</summary>
    public List<WatchFinding> SourceRisks() => SourceWatch.Assess(Ledger);
}
