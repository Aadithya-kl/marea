using System.Buffers.Binary;
using System.Security.Cryptography;
using Marea.Core;

namespace Marea.Tests;

public sealed class Bf16Tests
{
    [Theory]
    [InlineData(1.0f, 0x3F80)]
    [InlineData(-2.0f, 0xC000)]
    [InlineData(0.0f, 0x0000)]
    [InlineData(float.PositiveInfinity, 0x7F80)]
    [InlineData(float.NaN, 0x7FC0)]
    public void ConvertsKnownValues(float value, int expected)
    {
        var f = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(f, value);
        var b = new byte[2];
        Bf16.FromF32(f, b);
        Assert.Equal(expected, BinaryPrimitives.ReadUInt16LittleEndian(b));
    }

    [Fact]
    public void RoundsHalfwayToEven()
    {
        // 0x3F808000 is exactly halfway between bf16 0x3F80 and 0x3F81: ties go to the even 0x3F80.
        var f = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(f, 0x3F808000);
        var b = new byte[2];
        Bf16.FromF32(f, b);
        Assert.Equal(0x3F80, BinaryPrimitives.ReadUInt16LittleEndian(b));
    }
}

public sealed class RecipeTests : IDisposable
{
    private readonly string _dir = TestFiles.TempDir("recipe");

    [Fact]
    public async Task Bf16FileIsRebuiltBitExactFromF32Shards()
    {
        var w1 = TestFiles.RandomF32(5000, 1);
        var w2 = TestFiles.RandomF32(3001, 2);
        TestFiles.WriteSafetensors(Path.Combine(_dir, "s1.safetensors"), ("a", "F32", [5000], w1));
        TestFiles.WriteSafetensors(Path.Combine(_dir, "s2.safetensors"), ("b", "F32", [3001], w2));
        var b1 = new byte[10000];
        var b2 = new byte[6002];
        Bf16.FromF32(w1, b1);
        Bf16.FromF32(w2, b2);
        var target = Path.Combine(_dir, "t.safetensors");
        TestFiles.WriteSafetensors(target, ("b", "BF16", [3001], b2), ("a", "BF16", [5000], b1));
        var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target)));
        var all = new[] { "s1", "s2", "t" }.Select(n => SafetensorsFile.TryRead(Path.Combine(_dir, n + ".safetensors"))!).ToList();

        var recipe = RecipeBuilder.TryBuild(all[2], sha, all);

        Assert.NotNull(recipe);
        Assert.True(await RecipeBuilder.VerifyAsync(recipe!));
        await using var src = new RecipeRangeSource(recipe!);
        var mid = new byte[777];
        Assert.Equal(777, await src.ReadFullyAsync(12345, mid, default));
        Assert.Equal(File.ReadAllBytes(target).AsSpan(12345, 777).ToArray(), mid);
    }

    [Fact]
    public void RefusesWhenATensorHasNoCounterpart()
    {
        TestFiles.WriteSafetensors(Path.Combine(_dir, "s.safetensors"), ("a", "F32", [10], TestFiles.RandomF32(10, 3)));
        TestFiles.WriteSafetensors(Path.Combine(_dir, "t.safetensors"), ("zzz", "BF16", [10], new byte[20]));
        var all = new[] { "s", "t" }.Select(n => SafetensorsFile.TryRead(Path.Combine(_dir, n + ".safetensors"))!).ToList();
        Assert.Null(RecipeBuilder.TryBuild(all[1], "x", all));
    }

    [Fact]
    public async Task VerificationFailsWhenRoundingDiffers()
    {
        var w = TestFiles.RandomF32(100, 4);
        TestFiles.WriteSafetensors(Path.Combine(_dir, "s.safetensors"), ("a", "F32", [100], w));
        var truncated = new byte[200];
        for (var i = 0; i < 100; i++)
        {
            truncated[i * 2] = w[i * 4 + 2];      // plain truncation instead of round-to-nearest-even
            truncated[i * 2 + 1] = w[i * 4 + 3];
        }
        var target = Path.Combine(_dir, "t.safetensors");
        TestFiles.WriteSafetensors(target, ("a", "BF16", [100], truncated));
        var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target)));
        var all = new[] { "s", "t" }.Select(n => SafetensorsFile.TryRead(Path.Combine(_dir, n + ".safetensors"))!).ToList();
        var recipe = RecipeBuilder.TryBuild(all[1], sha, all)!;
        Assert.False(await RecipeBuilder.VerifyAsync(recipe));
    }

    public void Dispose() => Directory.Delete(_dir, true);
}

public sealed class SafetyGateTests : IDisposable
{
    private readonly string _dir = TestFiles.TempDir("gate");
    private readonly Ledger _ledger;
    private readonly MareaSettings _settings;

    public SafetyGateTests()
    {
        _ledger = new Ledger(Path.Combine(_dir, "db", "t.db"));
        _settings = new MareaSettings { ManagedRoots = [_dir], MinIdleDays = 7, HuggingFaceRepos = [] };
    }

    private FileRecord Add(string name, string sha, DateTime lastUse, Residency res = Residency.Local)
    {
        var f = new FileRecord
        {
            Path = Path.Combine(_dir, name), Root = _dir, Size = 1 << 30, Sha256 = sha,
            MTimeTicks = lastUse.Ticks, LastAccessUtc = lastUse, Residency = res,
        };
        _ledger.Upsert(f);
        return f;
    }

    [Fact]
    public void UniqueFileIsNeverAllowed()
    {
        var old = DateTime.UtcNow.AddDays(-60);
        Add("u.bin", new string('a', 64), old);
        var v = new SafetyGate(_ledger, _settings).EvaluateAll(DateTime.UtcNow).Single();
        Assert.False(v.Allowed);
        Assert.Contains(v.Blockers, b => b.Code == BlockerCode.Unique);
    }

    [Fact]
    public void TwoCopiesNeverLeaveTogetherWithoutExternalSource()
    {
        var old = DateTime.UtcNow.AddDays(-60);
        Add("a.bin", new string('b', 64), old);
        Add("b.bin", new string('b', 64), old);
        var gate = new SafetyGate(_ledger, _settings);
        Assert.All(gate.EvaluateAll(DateTime.UtcNow), v => Assert.True(v.Allowed));

        var plan = new TidePlanner(gate, _ledger).Plan(0, DateTime.UtcNow);

        Assert.Single(plan);
    }

    [Fact]
    public void BothCopiesMayLeaveWhenTheHubHasTheContent()
    {
        var old = DateTime.UtcNow.AddDays(-60);
        var sha = new string('c', 64);
        Add("a.bin", sha, old);
        Add("b.bin", sha, old);
        _ledger.PutSource(new Source(sha, SourceKind.HuggingFace, "o/r@abc:a.bin", 1 << 30, DateTime.UtcNow));
        var plan = new TidePlanner(new SafetyGate(_ledger, _settings), _ledger).Plan(0, DateTime.UtcNow);
        Assert.Equal(2, plan.Count);
    }

    [Fact]
    public void RecentlyUsedFileStays()
    {
        var sha = new string('d', 64);
        Add("a.bin", sha, DateTime.UtcNow.AddDays(-1));
        _ledger.PutSource(new Source(sha, SourceKind.HuggingFace, "o/r@abc:a.bin", 1 << 30, DateTime.UtcNow));
        var v = new SafetyGate(_ledger, _settings).EvaluateAll(DateTime.UtcNow).Single();
        Assert.Contains(v.Blockers, b => b.Code == BlockerCode.RecentlyUsed);
    }

    [Fact]
    public void LastCopyOfAnEvaporatedDuplicateIsPinned()
    {
        var old = DateTime.UtcNow.AddDays(-60);
        var sha = new string('e', 64);
        Add("gone.bin", sha, old, Residency.Evaporated);
        Add("keeper.bin", sha, old);
        _ledger.PutSource(new Source(sha, SourceKind.Mirror, "x", 1, DateTime.UtcNow));
        _ledger.DropSource(new Source(sha, SourceKind.Mirror, "x", 1, DateTime.UtcNow));
        var v = new SafetyGate(_ledger, _settings).EvaluateAll(DateTime.UtcNow).Single();
        Assert.Contains(v.Blockers, b => b.Code == BlockerCode.Pinned);
    }

    [Fact]
    public void InstantOnlyPlanSkipsFilesThatNeedADownload()
    {
        var old = DateTime.UtcNow.AddDays(-60);
        Add("dup-a.bin", new string('1', 64), old);
        Add("dup-b.bin", new string('1', 64), old);
        var hubSha = new string('2', 64);
        Add("public.bin", hubSha, old);
        _ledger.PutSource(new Source(hubSha, SourceKind.HuggingFace, "o/r@abc:public.bin", 1 << 30, DateTime.UtcNow));
        var planner = new TidePlanner(new SafetyGate(_ledger, _settings), _ledger);

        var instant = planner.Plan(0, DateTime.UtcNow, instantOnly: true);
        var all = planner.Plan(0, DateTime.UtcNow);

        Assert.Single(instant);
        Assert.Equal(ReturnPath.LocalCopy, instant[0].Path);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.Path == ReturnPath.Download);
    }

    [Fact]
    public void ExpectedCostPrefersStaleCheapFiles()
    {
        Assert.True(TidePlanner.UseProbability(60) < TidePlanner.UseProbability(1));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }
}

public sealed class RootRulesTests
{
    [Fact]
    public void RejectsSystemAndBroadFolders()
    {
        Assert.NotNull(FileSystemFacts.RejectRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows), []));
        Assert.NotNull(FileSystemFacts.RejectRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), []));
        Assert.NotNull(FileSystemFacts.RejectRoot(@"C:\", []));
    }

    [Fact]
    public void RejectsOverlappingRoots()
    {
        var d = TestFiles.TempDir("roots");
        var sub = Directory.CreateDirectory(Path.Combine(d, "sub")).FullName;
        try
        {
            Assert.Null(FileSystemFacts.RejectRoot(d, []));
            Assert.NotNull(FileSystemFacts.RejectRoot(sub, [d]));
        }
        finally
        {
            Directory.Delete(d, true);
        }
    }

    [Fact]
    public void HubHostAllowListIsStrict()
    {
        Assert.True(HuggingFaceClient.IsAllowedHost(new Uri("https://cas-bridge.xethub.hf.co/x")));
        Assert.True(HuggingFaceClient.IsAllowedHost(new Uri("https://huggingface.co/api")));
        Assert.False(HuggingFaceClient.IsAllowedHost(new Uri("http://huggingface.co/api")));
        Assert.False(HuggingFaceClient.IsAllowedHost(new Uri("https://huggingface.co.evil.com/")));
        Assert.False(HuggingFaceClient.IsAllowedHost(new Uri("https://evilhf.co/")));
    }
}
