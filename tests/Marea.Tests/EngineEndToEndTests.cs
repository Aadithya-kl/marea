using System.Security.Cryptography;
using Marea.CloudFiles;
using Marea.Core;

namespace Marea.Tests;

/// <summary>Full pipeline on real folders with the real cloud filter: scan, sources, gate,
/// evaporation, transparent return on read, re-verification and the emergency restore.</summary>
[Trait("Category", "CloudFilter")]
public sealed class EngineEndToEndTests : IDisposable
{
    private readonly string _data = TestFiles.TempDir("data");
    private readonly string _root = TestFiles.TempDir("root");
    private readonly MareaEngine _engine;

    public EngineEndToEndTests()
    {
        var s = new MareaSettings { HuggingFaceRepos = [], MinIdleDays = 0, MinFileSizeMb = 1 };
        s.Save(_data);
        _engine = new MareaEngine(_data);
        Assert.Null(_engine.AddRoot(_root));
        Assert.True(_engine.StartProvider());
    }

    [Fact]
    public async Task DuplicateEvaporatesAndComesBackOnRead()
    {
        var data = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 77);
        File.WriteAllBytes(Path.Combine(_root, "keep.bin"), data);
        Directory.CreateDirectory(Path.Combine(_root, "copy"));
        var dup = Path.Combine(_root, "copy", "dup.bin");
        File.WriteAllBytes(dup, data);

        await _engine.ScanAsync();
        await _engine.RefreshSourcesAsync();
        var plan = _engine.Plan(0);
        Assert.Single(plan); // one copy must stay
        var (done, _) = await _engine.EvaporateAsync(plan.Select(p => p.File));
        Assert.Equal(1, done);
        var gone = plan[0].File.Path;
        Assert.Equal(PlaceholderState.Dehydrated, Placeholder.GetState(gone));

        Assert.Equal(data, await File.ReadAllBytesAsync(gone));
        await WaitFor(() => _engine.Ledger.Get(gone)!.Residency == Residency.Local);
        Assert.Contains(_engine.Ledger.Journal(), j => j.Action == "j.returned_verified");
    }

    [Fact]
    public async Task Bf16CopyIsRebuiltFromF32WhenRead()
    {
        var w = TestFiles.RandomF32(600_000, 7);
        var bf = new byte[1_200_000];
        Bf16.FromF32(w, bf);
        Directory.CreateDirectory(Path.Combine(_root, "fp32"));
        Directory.CreateDirectory(Path.Combine(_root, "bf16"));
        TestFiles.WriteSafetensors(Path.Combine(_root, "fp32", "model.safetensors"), ("layer.w", "F32", [600_000], w));
        var target = Path.Combine(_root, "bf16", "model.safetensors");
        TestFiles.WriteSafetensors(target, ("layer.w", "BF16", [600_000], bf));
        var expected = await File.ReadAllBytesAsync(target);

        await _engine.ScanAsync();
        await _engine.RefreshSourcesAsync();
        Assert.Equal(Recoverability.Derivable, _engine.Ledger.Get(target)!.Class);
        var plan = _engine.Plan(0);
        Assert.Contains(plan, p => p.File.Path == target);
        Assert.DoesNotContain(plan, p => p.File.Path.Contains("fp32")); // the fp32 is unique
        await _engine.EvaporateAsync(plan.Where(p => p.File.Path == target).Select(p => p.File));

        Assert.Equal(expected, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task EmergencyRestoreLeavesOnlyOrdinaryFiles()
    {
        var data = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        File.WriteAllBytes(Path.Combine(_root, "a.bin"), data);
        File.WriteAllBytes(Path.Combine(_root, "b.bin"), data);
        await _engine.ScanAsync();
        await _engine.RefreshSourcesAsync();
        await _engine.EvaporateAsync(_engine.Plan(0).Select(p => p.File));

        var problem = await _engine.RestoreEverythingAsync();
        Assert.True(problem is null, problem);

        foreach (var f in Directory.GetFiles(_root))
        {
            Assert.Equal(PlaceholderState.NormalFile, Placeholder.GetState(f));
            Assert.Equal(data, await File.ReadAllBytesAsync(f));
        }
        Assert.False(SyncRoot.IsMareaRoot(_root));
    }

    [Fact]
    public async Task FolderIsNeverDetachedWhilePlaceholdersRemain()
    {
        var data = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        File.WriteAllBytes(Path.Combine(_root, "a.bin"), data);
        File.WriteAllBytes(Path.Combine(_root, "b.bin"), data);
        await _engine.ScanAsync();
        await _engine.RefreshSourcesAsync();
        await _engine.EvaporateAsync(_engine.Plan(0).Select(p => p.File));
        var gone = Assert.Single(MareaEngine.PlaceholdersUnder(_root));

        // Forget the ledger entry: a careless "detach" would now let Windows delete the placeholder.
        _engine.Ledger.Remove(gone);
        var problem = await _engine.RestoreEverythingAsync();

        Assert.NotNull(problem);
        Assert.True(SyncRoot.IsMareaRoot(_root));
        Assert.True(File.Exists(gone));
        Assert.Equal(data, await File.ReadAllBytesAsync(gone));
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        for (var i = 0; i < 100 && !cond(); i++)
        {
            await Task.Delay(100);
        }
        Assert.True(cond());
    }

    public void Dispose()
    {
        try { _engine.RestoreEverythingAsync().GetAwaiter().GetResult(); } catch (Exception) { }
        // Belt and braces: never leave a registered sync root or a placeholder behind on the machine.
        foreach (var p in MareaEngine.PlaceholdersUnder(_root).ToList())
        {
            try { Placeholder.Hydrate(p); Placeholder.Revert(p); } catch (Exception) { }
        }
        _engine.Dispose();
        try { if (SyncRoot.IsMareaRoot(_root)) SyncRoot.Unregister(_root); } catch (CloudFilesException) { }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
        try { Directory.Delete(_data, true); } catch (IOException) { }
    }
}
