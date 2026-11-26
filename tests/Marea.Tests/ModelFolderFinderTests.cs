using Marea.Core;

namespace Marea.Tests;

/// <summary>First-run discovery of model folders, including the default folders of popular apps.</summary>
public sealed class ModelFolderFinderTests : IDisposable
{
    private readonly string _dir = TestFiles.TempDir("finder");

    public void Dispose() => Directory.Delete(_dir, true);

    private string Make(string rel, int mb)
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[mb << 20]);
        return p;
    }

    [Fact]
    public void FindsLmStudioAndTheHuggingFaceCacheFromKnownLocations()
    {
        Make(@"home\.lmstudio\models\org\chat-q4.gguf", 2);
        Make(@"home\.lmstudio\models\org\chat-q8.gguf", 2);
        Make(@"hf\hub\models--org--name\blobs\0a1b2c3d4e5f", 4);
        Make(@"home\.lmstudio\models\tiny.gguf", 0);
        var lm = Path.Combine(_dir, @"home\.lmstudio\models");
        var hub = Path.Combine(_dir, @"hf\hub");
        var found = ModelFolderFinder.Find([], minBytes: 3 << 20, knownFolders: [lm, hub, Path.Combine(_dir, "missing")], minFileBytes: 1 << 20);
        Assert.Equal(2, found.Count);
        Assert.Equal(2, found.Single(f => f.Path == lm).ModelFiles);
        Assert.Equal(4L << 20, found.Single(f => f.Path == hub).ModelBytes);
    }

    [Fact]
    public void KnownFolderInsideAFoundFolderIsNotProposedTwice()
    {
        Make(@"work\models\a.safetensors", 4);
        Make(@"work\models\lm\b.gguf", 4);
        var found = ModelFolderFinder.Find([Path.Combine(_dir, "work")], minBytes: 3 << 20,
            knownFolders: [Path.Combine(_dir, @"work\models\lm")], minFileBytes: 1 << 20);
        Assert.Single(found);
        Assert.Equal(Path.Combine(_dir, @"work\models"), found[0].Path);
    }

    [Fact]
    public void KnownAppFoldersCoverLmStudioJanGpt4AllAndHuggingFace()
    {
        var all = ModelFolderFinder.KnownAppFolders().ToList();
        Assert.Contains(all, p => p.EndsWith(Path.Combine(".lmstudio", "models"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, p => p.Contains("GPT4All", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, p => p.Contains("Jan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, p => p.EndsWith("hub", StringComparison.OrdinalIgnoreCase));
    }
}
