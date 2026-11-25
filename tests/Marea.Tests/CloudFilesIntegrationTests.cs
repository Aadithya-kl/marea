using System.Security.Cryptography;
using Marea.CloudFiles;

namespace Marea.Tests;

/// <summary>Real round-trip through the Windows cloud filter driver on a temporary folder.</summary>
[Trait("Category", "CloudFilter")]
public sealed class CloudFilesIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "marea-it-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _src;

    public CloudFilesIntegrationTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        _src = _root + "-src";
        Directory.CreateDirectory(_src);
    }

    [Fact]
    public void EvaporatedFileRehydratesWithIdenticalBytesOnRead()
    {
        var data = RandomNumberGenerator.GetBytes(10 * 1024 * 1024 + 123);
        var target = Path.Combine(_root, "sub", "b.bin");
        var source = Path.Combine(_src, "a.bin");
        File.WriteAllBytes(target, data);
        File.WriteAllBytes(source, data);

        SyncRoot.Register(_root, "test");
        using (ProviderConnection.Connect(_root, req => StreamFromAsync(source, req)))
        {
            using (var h = File.OpenHandle(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Placeholder.ConvertAndDehydrate(h, SHA256.HashData(data));
            }
            Assert.Equal(PlaceholderState.Dehydrated, Placeholder.GetState(target));
            Assert.Equal(data.Length, new FileInfo(target).Length);
            Assert.True(Placeholder.SizeOnDisk(target) < data.Length / 10);

            var read = File.ReadAllBytes(target);

            Assert.Equal(data, read);
            // Windows clears the "recall" attribute shortly after the last range lands.
            for (var i = 0; i < 50 && Placeholder.GetState(target) != PlaceholderState.Hydrated; i++)
            {
                Thread.Sleep(100);
            }
            Assert.Equal(PlaceholderState.Hydrated, Placeholder.GetState(target));
            Placeholder.Revert(target);
            Assert.Equal(PlaceholderState.NormalFile, Placeholder.GetState(target));
        }
    }

    [Fact]
    public void ForeignOrUnregisteredFolderIsNotMareaRoot()
    {
        Assert.False(SyncRoot.IsMareaRoot(_src));
        Assert.False(SyncRoot.IsForeignRoot(_src));
    }

    private static async Task StreamFromAsync(string source, FetchRequest req)
    {
        var start = req.RequiredOffset & ~4095L;
        await using var fs = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        fs.Position = start;
        var buf = new byte[1 << 20];
        var pos = start;
        int n;
        while ((n = await fs.ReadAsync(buf)) > 0)
        {
            req.Transfer(pos, buf.AsSpan(0, n));
            pos += n;
        }
    }

    public void Dispose()
    {
        try { SyncRoot.Unregister(_root); } catch (CloudFilesException) { }
        try { Directory.Delete(_root, true); } catch (IOException) { }
        try { Directory.Delete(_src, true); } catch (IOException) { }
    }
}
