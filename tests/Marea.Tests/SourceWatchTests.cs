using Marea.Core;

namespace Marea.Tests;

/// <summary>Endangered-model watch: health rules, stored states, findings and how the gate reacts.</summary>
public sealed class SourceWatchTests : IDisposable
{
    private readonly string _dir = TestFiles.TempDir("watch");
    private readonly Ledger _ledger;
    private readonly MareaSettings _settings;
    private static readonly string ShaA = new('a', 64);
    private static readonly string ShaB = new('b', 64);
    private static readonly HashSet<string> NoFiles = [];

    public SourceWatchTests()
    {
        _ledger = new Ledger(Path.Combine(_dir, "db", "t.db"));
        _settings = new MareaSettings { ManagedRoots = [_dir], MinIdleDays = 0, HuggingFaceRepos = [] };
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // A pooled handle may linger; the temp folder is harmless.
        }
    }

    private FileRecord Add(string name, string sha, Residency res = Residency.Local)
    {
        var old = DateTime.UtcNow.AddDays(-90);
        var f = new FileRecord
        {
            Path = Path.Combine(_dir, name), Root = _dir, Size = 1 << 20, Sha256 = sha,
            MTimeTicks = old.Ticks, LastAccessUtc = old, Residency = res, Class = Recoverability.Reobtainable,
        };
        _ledger.Upsert(f);
        return f;
    }

    private Source Hub(string sha, string repo = "org/model", string path = "model.safetensors")
    {
        var s = new Source(sha, SourceKind.HuggingFace, $"{repo}@{new string('c', 40)}:{path}", 1 << 20, DateTime.UtcNow);
        _ledger.PutSource(s);
        return s;
    }

    private sealed class FakeProbe(ProbeResult file, RepoView repo) : ISourceProbe
    {
        public int Calls;
        public Task<ProbeResult> FileAsync(HubFile f, CancellationToken ct) { Calls++; return Task.FromResult(file); }
        public Task<RepoView> RepoAsync(string r, CancellationToken ct) => Task.FromResult(repo);
        public Task<ProbeResult> BlobAsync(OllamaBlob b, CancellationToken ct) => Task.FromResult(ProbeResult.Ok);
        public Task<TagView> TagAsync(OllamaTag t, string sha, CancellationToken ct) => Task.FromResult(new TagView(t.Name, ProbeResult.Ok, true));
        public IReadOnlyList<OllamaTag> TagsOf(string sha) => [];
    }

    private static RepoView Latest(params string[] shas) => new(ProbeResult.Ok, false, false, shas.ToHashSet());

    [Fact]
    public void HubRulesSeparateEarlyWarningsFromLoss()
    {
        Assert.Equal(SourceHealth.Healthy, HealthRules.ForHub(ProbeResult.Ok, Latest(ShaA), ShaA).Item1);
        Assert.Equal(SourceHealth.AtRisk, HealthRules.ForHub(ProbeResult.Ok, Latest(ShaB), ShaA).Item1);
        Assert.Equal(SourceHealth.AtRisk, HealthRules.ForHub(ProbeResult.Ok, Latest(ShaA) with { Gated = true }, ShaA).Item1);
        Assert.Equal(SourceHealth.Gone, HealthRules.ForHub(ProbeResult.Missing, Latest(ShaA), ShaA).Item1);
        Assert.Equal(SourceHealth.Gone, HealthRules.ForHub(ProbeResult.Restricted, Latest(ShaA), ShaA).Item1);
        Assert.Equal(SourceHealth.Unreachable, HealthRules.ForHub(ProbeResult.Error, Latest(ShaA), ShaA).Item1);
        // The latest revision could not be listed: no conclusion about upstream, the file itself answers.
        Assert.Equal(SourceHealth.Healthy, HealthRules.ForHub(ProbeResult.Ok, new RepoView(ProbeResult.Error, false, false, NoFiles), ShaA).Item1);
    }

    [Fact]
    public void OllamaTagRemovedIsAnEarlyWarningButAnUpdateIsNot()
    {
        Assert.Equal(SourceHealth.AtRisk, HealthRules.ForOllama(ProbeResult.Ok, [new TagView("m:1", ProbeResult.Missing, false)]).Item1);
        Assert.Equal(SourceHealth.Healthy, HealthRules.ForOllama(ProbeResult.Ok, [new TagView("m:1", ProbeResult.Ok, false)]).Item1);
        Assert.Equal(SourceHealth.Gone, HealthRules.ForOllama(ProbeResult.Missing, []).Item1);
    }

    [Fact]
    public void UnreachableNeverOverwritesAConclusiveState()
    {
        var now = DateTime.UtcNow;
        _ledger.PutHealth("x", SourceHealth.AtRisk, "d", now.AddDays(-2));
        var (status, changed) = _ledger.PutHealth("x", SourceHealth.Unreachable, "", now);
        Assert.Equal(SourceHealth.AtRisk, status.State);
        Assert.False(changed);
        Assert.Equal(now.AddDays(-2), status.SinceUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task EvaporatedFileWhoseSourceLeftTheLatestRevisionIsRescued()
    {
        Add("gone.safetensors", ShaA, Residency.Evaporated);
        Hub(ShaA);
        var report = await new SourceWatch(_ledger, new FakeProbe(ProbeResult.Ok, Latest(ShaB))).CheckAsync();
        var f = Assert.Single(report.Findings);
        Assert.Equal(WatchAction.Rescue, f.Action);
        Assert.Equal(1, report.Changed);
        Assert.Contains(_ledger.Journal(), j => j.Action == "j.source_at_risk");

        var again = await new SourceWatch(_ledger, new FakeProbe(ProbeResult.Ok, Latest(ShaB))).CheckAsync();
        Assert.Equal(0, again.Changed); // logged once, not on every run
    }

    [Fact]
    public async Task EvaporatedFileWithEverySourceGoneIsReportedLost()
    {
        Add("lost.safetensors", ShaA, Residency.Evaporated);
        Hub(ShaA);
        var report = await new SourceWatch(_ledger, new FakeProbe(ProbeResult.Missing, Latest())).CheckAsync();
        Assert.Equal(WatchAction.Lost, Assert.Single(report.Findings).Action);
    }

    [Fact]
    public async Task LocalFileWithDisappearingSourceIsProtectedAndCannotEvaporate()
    {
        var f = Add("last.safetensors", ShaA);
        Hub(ShaA);
        var gate = new SafetyGate(_ledger, _settings);
        Assert.True(gate.EvaluateAll(DateTime.UtcNow).Single().Allowed); // never checked: trusted

        var report = await new SourceWatch(_ledger, new FakeProbe(ProbeResult.Ok, Latest() with { Disabled = true })).CheckAsync();
        Assert.Equal(WatchAction.Protect, Assert.Single(report.Findings).Action);
        var v = gate.EvaluateAll(DateTime.UtcNow).Single();
        Assert.False(v.Allowed);
        Assert.Contains(v.Blockers, b => b.Code == BlockerCode.SourceAtRisk);
        Assert.Equal(f.Path, v.File.Path);
    }

    [Fact]
    public async Task ALocalCopyKeepsTheEvaporatedFileSafeAndIsItselfProtected()
    {
        Add("a.safetensors", ShaA, Residency.Evaporated);
        var copy = Add("copy.safetensors", ShaA);
        Hub(ShaA);
        var report = await new SourceWatch(_ledger, new FakeProbe(ProbeResult.Missing, Latest())).CheckAsync();
        var only = Assert.Single(report.Findings); // the evaporated file can come back from the copy...
        Assert.Equal(copy.Path, only.File.Path);  // ...which is now the last copy anywhere
        Assert.Equal(WatchAction.Protect, only.Action);
    }

    [Fact]
    public async Task LocalCopyOfAnEvaporatedFileStaysPinnedWhenThePublicSourceIsDisappearing()
    {
        Add("a.safetensors", ShaA, Residency.Evaporated);
        var copy = Add("copy.safetensors", ShaA);
        Hub(ShaA);
        var gate = new SafetyGate(_ledger, _settings);
        Assert.DoesNotContain(copy.Path, gate.PinnedPaths(_ledger.Files()));

        await new SourceWatch(_ledger, new FakeProbe(ProbeResult.Ok, Latest(ShaB))).CheckAsync();
        Assert.Contains(copy.Path, gate.PinnedPaths(_ledger.Files()));
    }

    [Fact]
    public async Task FileWhosePublicSourcesAreAllGoneBecomesUnique()
    {
        var f = Add("x.safetensors", ShaA);
        Hub(ShaA);
        await new SourceWatch(_ledger, new FakeProbe(ProbeResult.Restricted, Latest())).CheckAsync();
        var (cls, label) = new SourceResolver(_ledger, _settings, new HuggingFaceClient()).Classify(f);
        Assert.Equal(Recoverability.Unique, cls);
        Assert.StartsWith("src.public_gone", label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachableHubConcludesNothing()
    {
        Add("a.safetensors", ShaA, Residency.Evaporated);
        Hub(ShaA);
        var report = await new SourceWatch(_ledger, new FakeProbe(ProbeResult.Error, new RepoView(ProbeResult.Error, false, false, NoFiles))).CheckAsync();
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.Changed);
    }
}
