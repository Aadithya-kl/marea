namespace Marea.Core;

/// <summary>Network probes used by the watch (an interface so the rules can be tested offline).</summary>
public interface ISourceProbe
{
    /// <summary>Is the pinned Hub file still served?</summary>
    Task<ProbeResult> FileAsync(HubFile file, CancellationToken ct);
    /// <summary>Flags and latest files of a Hub repository.</summary>
    Task<RepoView> RepoAsync(string repo, CancellationToken ct);
    /// <summary>Is the Ollama blob still served?</summary>
    Task<ProbeResult> BlobAsync(OllamaBlob blob, CancellationToken ct);
    /// <summary>Does the local tag still exist upstream, and does it still use this blob?</summary>
    Task<TagView> TagAsync(OllamaTag tag, string sha256, CancellationToken ct);
    /// <summary>Local Ollama tags that use a blob.</summary>
    IReadOnlyList<OllamaTag> TagsOf(string sha256);
}

/// <summary>What the watch recommends for one file.</summary>
public enum WatchAction
{
    /// <summary>Evaporated, its public source is disappearing but still answers: bring it back now.</summary>
    Rescue,
    /// <summary>On disk, and its public source is disappearing or gone: this may be the last copy.</summary>
    Protect,
    /// <summary>Evaporated and every source is gone: it cannot come back until a source returns.</summary>
    Lost,
}

/// <summary>A file whose way back depends only on public sources that are at risk or gone.</summary>
/// <param name="File">The file.</param>
/// <param name="State">Best state among its public sources (AtRisk or Gone).</param>
/// <param name="Detail">Coded explanation from the source.</param>
/// <param name="Action">What to do about it.</param>
public sealed record WatchFinding(FileRecord File, SourceHealth State, string Detail, WatchAction Action);

/// <summary>Result of one watch run.</summary>
/// <param name="SourcesChecked">Public sources probed.</param>
/// <param name="Changed">Sources whose state changed in this run.</param>
/// <param name="Findings">Files that need attention.</param>
public sealed record WatchReport(int SourcesChecked, int Changed, List<WatchFinding> Findings);

/// <summary>Watches public sources: a model that silently disappears from Hugging Face or the Ollama
/// registry would make an evaporated file impossible to bring back, so the watch looks for early
/// signs (removed from the latest revision, repository gated or disabled, tag deleted) while the
/// pinned bytes are still served.</summary>
public sealed class SourceWatch(Ledger ledger, ISourceProbe probe)
{
    /// <summary>Probes every public source of tracked files, stores their health, returns findings.</summary>
    public async Task<WatchReport> CheckAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var files = ledger.Files().Where(f => f.Sha256.Length == 64).ToList();
        var sources = files.Select(f => f.Sha256).Distinct().SelectMany(ledger.SourcesOf)
            .Where(s => s.Kind is SourceKind.HuggingFace or SourceKind.Ollama).ToList();
        var now = DateTime.UtcNow;
        var changed = 0;
        var done = 0;
        void Record(Source s, SourceHealth state, string detail)
        {
            var (status, isNew) = ledger.PutHealth(s.Locator, state, detail, now);
            progress?.Report(new ScanProgress(Loc.T("phase.watch"), s.Locator, ++done, sources.Count));
            if (isNew && status.State is SourceHealth.AtRisk or SourceHealth.Gone)
            {
                changed++;
                ledger.Log(status.State == SourceHealth.Gone ? "j.source_gone" : "j.source_at_risk", s.Locator, s.Size, false, status.Detail);
            }
        }
        foreach (var repo in sources.Where(s => s.Kind == SourceKind.HuggingFace)
                     .GroupBy(s => HubFile.Parse(s.Locator, s.Sha256, s.Size).Repo, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var view = await probe.RepoAsync(repo.Key, ct);
            foreach (var s in repo)
            {
                var hubFile = HubFile.Parse(s.Locator, s.Sha256, s.Size);
                if (view.Flagged?.GetValueOrDefault(hubFile.Path) is { } level)
                {
                    ledger.PutHubScan(s.Sha256, level, repo.Key);
                }
                else if (view.ScansDone)
                {
                    ledger.PutHubScan(s.Sha256, "clean", repo.Key);
                }
                var file = await probe.FileAsync(hubFile, ct);
                var (state, detail) = HealthRules.ForHub(file, view, s.Sha256);
                Record(s, state, detail);
            }
        }
        foreach (var s in sources.Where(s => s.Kind == SourceKind.Ollama))
        {
            ct.ThrowIfCancellationRequested();
            var blob = await probe.BlobAsync(OllamaBlob.Parse(s.Locator, s.Size), ct);
            var tags = new List<TagView>();
            if (blob == ProbeResult.Ok)
            {
                foreach (var tag in probe.TagsOf(s.Sha256))
                {
                    tags.Add(await probe.TagAsync(tag, s.Sha256, ct));
                }
            }
            var (state, detail) = HealthRules.ForOllama(blob, tags);
            Record(s, state, detail);
        }
        return new WatchReport(sources.Count, changed, Assess(ledger, files));
    }

    /// <summary>Files whose only ways back are public sources at risk or gone (no local copy,
    /// recipe or mirror). Pure ledger read: no network.</summary>
    public static List<WatchFinding> Assess(Ledger ledger, List<FileRecord>? files = null)
    {
        files ??= ledger.Files();
        var findings = new List<WatchFinding>();
        foreach (var f in files.Where(f => f.Sha256.Length == 64))
        {
            var sources = ledger.SourcesOf(f.Sha256);
            var health = sources.Where(s => s.Kind is SourceKind.HuggingFace or SourceKind.Ollama)
                .Select(s => ledger.Health(s.Locator)).ToList();
            if (health.Count == 0 || health.Any(h => HealthRules.Usable(h?.State ?? SourceHealth.Unknown)))
            {
                continue;
            }
            var copy = files.Any(o => o.Sha256 == f.Sha256 && o.Residency == Residency.Local
                                      && !string.Equals(o.Path, f.Path, StringComparison.OrdinalIgnoreCase));
            var mirror = sources.Any(s => s.Kind == SourceKind.Mirror);
            var recipe = sources.Any(s => s.Kind == SourceKind.Recipe) && ledger.GetRecipe(f.Sha256) is { } json
                         && Recipe.FromJson(json).SourcesUnchanged();
            if (copy || mirror || recipe)
            {
                continue;
            }
            var best = health.OrderBy(h => h!.State).First()!;
            var action = f.Residency != Residency.Evaporated ? WatchAction.Protect
                : best.State == SourceHealth.AtRisk ? WatchAction.Rescue : WatchAction.Lost;
            findings.Add(new WatchFinding(f, best.State, best.Detail, action));
        }
        return findings;
    }
}

/// <summary>The real probes: Hugging Face and the Ollama registries, through their allow-listed clients.</summary>
public sealed class PublicSourceProbe(HuggingFaceClient hub, OllamaRegistry ollama, string ollamaModelsDir) : ISourceProbe
{
    private Dictionary<string, List<OllamaTag>>? _tags;

    /// <inheritdoc/>
    public Task<ProbeResult> FileAsync(HubFile file, CancellationToken ct) => hub.ProbeFileAsync(file, ct);

    /// <inheritdoc/>
    public Task<RepoView> RepoAsync(string repo, CancellationToken ct) => hub.RepoViewAsync(repo, ct);

    /// <inheritdoc/>
    public Task<ProbeResult> BlobAsync(OllamaBlob blob, CancellationToken ct) => ollama.ProbeBlobAsync(blob, ct);

    /// <inheritdoc/>
    public Task<TagView> TagAsync(OllamaTag tag, string sha256, CancellationToken ct) => ollama.ProbeTagAsync(tag, sha256, ct);

    /// <inheritdoc/>
    public IReadOnlyList<OllamaTag> TagsOf(string sha256)
    {
        _tags ??= OllamaNames.ReadTags(ollamaModelsDir);
        return _tags.TryGetValue(sha256, out var list) ? list : [];
    }
}
