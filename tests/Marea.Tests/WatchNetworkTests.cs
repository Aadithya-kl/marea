using Marea.Core;

namespace Marea.Tests;

/// <summary>Source-watch probes against the live Hugging Face Hub and Ollama registry (needs internet).
/// Run with: dotnet test --filter Category=Network</summary>
[Trait("Category", "Network")]
public sealed class WatchNetworkTests
{
    private const string Repo = "Qwen/Qwen3-Embedding-0.6B";

    [Fact]
    public async Task LivePublicFileIsHealthy()
    {
        using var hub = new HuggingFaceClient();
        var view = await hub.RepoViewAsync(Repo);
        Assert.Equal(ProbeResult.Ok, view.Result);
        Assert.NotEmpty(view.LatestShas);
        var file = (await hub.ListLfsFilesAsync(Repo)).MinBy(f => f.Size)!;
        Assert.Equal(ProbeResult.Ok, await hub.ProbeFileAsync(file));
        Assert.Equal(SourceHealth.Healthy, HealthRules.ForHub(ProbeResult.Ok, view, file.Sha256).Item1);
    }

    [Fact]
    public async Task HubSecurityScanResultsAreRead()
    {
        // Hugging Face's own public test repository with deliberately flagged files.
        using var hub = new HuggingFaceClient();
        var view = await hub.RepoViewAsync("mcpotato/42-eicar-street");
        Assert.True(view.ScansDone);
        Assert.Contains(view.Flagged!, kv => kv.Value == "unsafe");
    }

    [Fact]
    public async Task MissingRepositoryIsNotServed()
    {
        using var hub = new HuggingFaceClient();
        var ghost = new HubFile("marea-test-nobody/does-not-exist-" + Guid.NewGuid().ToString("N")[..8],
            new string('0', 40), "model.safetensors", new string('a', 64), 1);
        Assert.Contains(await hub.ProbeFileAsync(ghost), new[] { ProbeResult.Missing, ProbeResult.Restricted });
        Assert.Contains((await hub.RepoViewAsync(ghost.Repo)).Result, new[] { ProbeResult.Missing, ProbeResult.Restricted });
    }

    [Fact]
    public async Task OllamaLibraryTagIsPublishedAndUnknownTagIsMissing()
    {
        using var reg = new OllamaRegistry();
        var known = await reg.ProbeTagAsync(new OllamaTag("registry.ollama.ai", "library/all-minilm", "latest", "all-minilm:latest"), new string('a', 64));
        Assert.Equal(ProbeResult.Ok, known.Result);
        Assert.False(known.StillPointsHere);
        var unknown = await reg.ProbeTagAsync(new OllamaTag("registry.ollama.ai", "library/all-minilm", "no-such-tag-" + Guid.NewGuid().ToString("N")[..6], "x"), new string('a', 64));
        Assert.Equal(ProbeResult.Missing, unknown.Result);
    }
}
