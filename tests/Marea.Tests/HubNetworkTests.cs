using Marea.CloudFiles;
using Marea.Core;

namespace Marea.Tests;

/// <summary>Live round-trip against the public Hugging Face Hub (needs internet).
/// Run with: dotnet test --filter Category=Network</summary>
[Trait("Category", "Network")]
public sealed class HubNetworkTests : IDisposable
{
    private const string Repo = "Qwen/Qwen3-Embedding-0.6B";
    private readonly string _data = TestFiles.TempDir("hubdata");
    private readonly string _root = TestFiles.TempDir("hubroot");

    [Fact]
    public async Task PublicFileEvaporatesAndComesBackFromTheHub()
    {
        using var hub = new HuggingFaceClient();
        var smallest = (await hub.ListLfsFilesAsync(Repo)).Where(f => f.Size > 1 << 20).MinBy(f => f.Size)!;
        Assert.True(await hub.VerifyAsync(smallest));
        var local = Path.Combine(_root, Path.GetFileName(smallest.Path));
        await using (var s = await hub.OpenRangeAsync(smallest, 0, smallest.Size - 1, default))
        await using (var fs = File.Create(local))
        {
            await s.CopyToAsync(fs);
        }
        var original = await File.ReadAllBytesAsync(local);

        new MareaSettings { HuggingFaceRepos = [Repo], MinIdleDays = 0, MinFileSizeMb = 1 }.Save(_data);
        using var engine = new MareaEngine(_data);
        Assert.Null(engine.AddRoot(_root));
        Assert.True(engine.StartProvider());
        await engine.ScanAsync();
        await engine.RefreshSourcesAsync();
        var rec = engine.Ledger.Get(local)!;
        Assert.Equal(Recoverability.Reobtainable, rec.Class);
        var (done, kept) = await engine.EvaporateAsync([rec]);
        Assert.True(done == 1, string.Join("; ", kept.Select(k => k.Why)));
        Assert.True(Placeholder.SizeOnDisk(local) < original.Length / 10);

        Assert.Equal(original, await File.ReadAllBytesAsync(local));

        Assert.Null(await engine.RestoreEverythingAsync());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { SyncRoot.Unregister(_root); } catch (CloudFilesException) { }
        try { Directory.Delete(_root, true); } catch (IOException) { }
        try { Directory.Delete(_data, true); } catch (IOException) { }
    }
}
