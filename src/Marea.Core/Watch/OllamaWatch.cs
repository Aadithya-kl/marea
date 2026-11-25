using System.Net;
using System.Text.Json;

namespace Marea.Core;

/// <summary>Source-watch probes on the Ollama registries: is the blob still served, and do the
/// tags that use it still exist and still point at it?</summary>
public sealed partial class OllamaRegistry
{
    private const string ManifestType = "application/vnd.docker.distribution.manifest.v2+json";

    /// <summary>HEAD on the blob, without following the storage redirect.</summary>
    public async Task<ProbeResult> ProbeBlobAsync(OllamaBlob b, CancellationToken ct = default)
    {
        try
        {
            using var resp = await SafeHttp.SendAsync(_http, HttpMethod.Head, b.BlobUri, null, IsAllowedHost, ct, followRedirects: false);
            return resp.StatusCode is HttpStatusCode.OK or HttpStatusCode.TemporaryRedirect or HttpStatusCode.Found
                or HttpStatusCode.SeeOther or HttpStatusCode.PermanentRedirect
                ? ProbeResult.Ok
                : HuggingFaceClient.Classify(resp.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return ProbeResult.Error;
        }
    }

    /// <summary>Fetches the published manifest of a tag and checks whether it still lists the blob.</summary>
    public async Task<TagView> ProbeTagAsync(OllamaTag tag, string sha256, CancellationToken ct = default)
    {
        try
        {
            var uri = new Uri($"https://{tag.Host}/v2/{tag.Repository}/manifests/{Uri.EscapeDataString(tag.Tag)}");
            using var resp = await SafeHttp.SendAsync(_http, HttpMethod.Get, uri, null, IsAllowedHost, ct, accept: ManifestType);
            if (!resp.IsSuccessStatusCode)
            {
                return new TagView(tag.Name, HuggingFaceClient.Classify(resp.StatusCode), false);
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var digests = doc.RootElement.TryGetProperty("layers", out var layers)
                ? layers.EnumerateArray().Select(l => l.TryGetProperty("digest", out var d) ? d.GetString() : null)
                : [];
            return new TagView(tag.Name, ProbeResult.Ok, digests.Contains("sha256:" + sha256));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return new TagView(tag.Name, ProbeResult.Error, false);
        }
    }
}
