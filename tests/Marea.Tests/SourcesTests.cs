using System.Security.Cryptography;
using System.Text.Json;
using Marea.Core;

namespace Marea.Tests;

public sealed class RepoDiscoveryTests : IDisposable
{
    private readonly string _dir = TestFiles.TempDir("disc");

    [Fact]
    public void FindsReposInReadmeConfigAndGitRemotesButIgnoresLocalPaths()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "a"));
        File.WriteAllText(Path.Combine(_dir, "a", "README.md"), "---\nlicense: mit\nbase_model:\n  - Org/base-model\n  - Other/second\n---\n# hi");
        Directory.CreateDirectory(Path.Combine(_dir, "b"));
        File.WriteAllText(Path.Combine(_dir, "b", "config.json"), "{\"_name_or_path\": \"Qwen/Qwen3-Embedding-0.6B\"}");
        Directory.CreateDirectory(Path.Combine(_dir, "c"));
        File.WriteAllText(Path.Combine(_dir, "c", "config.json"), "{\"_name_or_path\": \"/root/data/repo/checkpoints/vae/\"}");
        Directory.CreateDirectory(Path.Combine(_dir, "d", ".git"));
        File.WriteAllText(Path.Combine(_dir, "d", ".git", "config"), "[remote \"origin\"]\n\turl = https://huggingface.co/black-forest-labs/FLUX.1-dev\n");

        var found = RepoDiscovery.LocalEvidence([_dir]);

        Assert.Contains("Org/base-model", found);
        Assert.Contains("Other/second", found);
        Assert.Contains("Qwen/Qwen3-Embedding-0.6B", found);
        Assert.Contains("black-forest-labs/FLUX.1-dev", found);
        Assert.DoesNotContain(found, r => r.Contains("root", StringComparison.Ordinal));
    }

    [Fact]
    public void RelatedFilterKeepsOnlyReposThatLookLikeLocalModels()
    {
        var local = new HashSet<string> { "qwen3-embedding-0.6b", "checkpoints" };
        Assert.True(RepoDiscovery.LooksRelated("Qwen/Qwen3-Embedding-0.6B", local));
        Assert.False(RepoDiscovery.LooksRelated("Qwen/Qwen2.5-72B-Instruct", local));
    }

    public void Dispose() => Directory.Delete(_dir, true);
}

public sealed class OllamaTests : IDisposable
{
    private readonly string _dir = TestFiles.TempDir("ollama");

    [Fact]
    public void ReadsLayersAndConfigFromSupportedRegistriesOnly()
    {
        var a = new string('a', 64);
        var b = new string('b', 64);
        var manifest = $$"""{"config":{"digest":"sha256:{{a}}","size":10},"layers":[{"digest":"sha256:{{b}}","size":2000}]}""";
        var ok = Path.Combine(_dir, "manifests", "registry.ollama.ai", "library", "smollm2", "135m");
        var hf = Path.Combine(_dir, "manifests", "hf.co", "LiquidAI", "LFM2-350M-GGUF", "Q4_K_M");
        var other = Path.Combine(_dir, "manifests", "evil.example", "x", "y", "z");
        foreach (var p in new[] { ok, hf, other })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, manifest);
        }

        var blobs = OllamaRegistry.ReadManifests(_dir);

        Assert.Equal(4, blobs.Count);
        Assert.Contains(blobs, x => x.Host == "registry.ollama.ai" && x.Repository == "library/smollm2" && x.Sha256 == b && x.Size == 2000);
        Assert.Contains(blobs, x => x.Host == "hf.co" && x.Repository == "LiquidAI/LFM2-350M-GGUF");
        Assert.DoesNotContain(blobs, x => x.Host == "evil.example");
        Assert.Equal(blobs[0], OllamaBlob.Parse(blobs[0].Locator, blobs[0].Size));
    }

    [Fact]
    public void AllowListPinsTheExactBlobStorageHost()
    {
        Assert.True(OllamaRegistry.IsAllowedHost(new Uri("https://registry.ollama.ai/v2/x")));
        Assert.True(OllamaRegistry.IsAllowedHost(new Uri($"https://{OllamaRegistry.OllamaBlobHost}/ollama/x")));
        Assert.False(OllamaRegistry.IsAllowedHost(new Uri("https://attacker.r2.cloudflarestorage.com/x")));
        Assert.False(OllamaRegistry.IsAllowedHost(new Uri("https://registry.ollama.ai.evil.com/x")));
    }

    public void Dispose() => Directory.Delete(_dir, true);
}

public sealed class UpdateCheckerTests
{
    [Fact]
    public void PicksTheHighestNewerNonDraftRelease()
    {
        using var doc = JsonDocument.Parse("""
            [{"tag_name":"v0.3.0","draft":true},{"tag_name":"v0.2.1"},{"tag_name":"v0.2.0-beta"},{"tag_name":"v0.1.0"}]
            """);
        var u = UpdateChecker.Newest(doc.RootElement, new Version(0, 1, 0));
        Assert.Equal(new Version(0, 2, 1), u!.Version);
        Assert.Equal("github.com", u.Url.Host);
        Assert.Null(UpdateChecker.Newest(doc.RootElement, new Version(0, 2, 1)));
    }
}

[Trait("Category", "Network")]
public sealed class OllamaNetworkTests
{
    [Fact]
    public async Task RegistryBlobIsVerifiedAndReadByDigest()
    {
        // License layer of library/smollm2:135m (public, a few KB).
        const string digest = "58d1e17ffe5109a7ae296caafcadfdbe6a7d176f0bc4ab01e12a689b0499d8bd";
        using var reg = new OllamaRegistry();
        var probe = new OllamaBlob("registry.ollama.ai", "library/smollm2", digest, 0);
        Assert.True(await reg.VerifyAsync(probe));
        using var http = SafeHttp.CreateClient();
        using var resp = await SafeHttp.SendAsync(http, HttpMethod.Get, probe.BlobUri, null, OllamaRegistry.IsAllowedHost, default);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal(digest, Convert.ToHexStringLower(SHA256.HashData(bytes)));

        await using var src = reg.Open(probe with { Size = bytes.Length });
        var tail = new byte[100];
        Assert.Equal(100, await src.ReadFullyAsync(bytes.Length - 100, tail, default));
        Assert.Equal(bytes[^100..], tail);
    }
}
