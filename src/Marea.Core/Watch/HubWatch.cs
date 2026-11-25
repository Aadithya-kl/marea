using System.Net;
using System.Text.Json;

namespace Marea.Core;

/// <summary>Source-watch probes on the Hub: is the pinned file still served, and does the repository
/// still publish it? Same allow-list and redirect rules as every other Hub request.</summary>
public sealed partial class HuggingFaceClient
{
    /// <summary>HEAD on the pinned file (no download).</summary>
    public async Task<ProbeResult> ProbeFileAsync(HubFile f, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, ResolveUri(f)), ct);
            if (resp.StatusCode is HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.OK)
            {
                var etag = Header(resp, "X-Linked-ETag")?.Trim('"');
                return etag is null || string.Equals(etag, f.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? ProbeResult.Ok
                    : ProbeResult.Missing; // the pinned path now answers with other bytes
            }
            return Classify(resp.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return ProbeResult.Error;
        }
    }

    /// <summary>Repository flags and the sha256 of every LFS file in its latest revision.</summary>
    public async Task<RepoView> RepoViewAsync(string repo, CancellationToken ct = default)
    {
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!RepoName().IsMatch(repo))
        {
            return new RepoView(ProbeResult.Missing, false, false, none);
        }
        try
        {
            using var info = await SendAsync(HttpMethod.Get, new Uri($"https://huggingface.co/api/models/{repo}"), null, ct);
            if (!info.IsSuccessStatusCode)
            {
                return new RepoView(Classify(info.StatusCode), false, false, none);
            }
            using var doc = JsonDocument.Parse(await info.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var gated = root.TryGetProperty("gated", out var g) && g.ValueKind is JsonValueKind.True or JsonValueKind.String;
            var disabled = root.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True;
            var latest = (await ListLfsFilesAsync(repo, ct)).Select(x => x.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new RepoView(ProbeResult.Ok, gated, disabled, latest);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return new RepoView(ProbeResult.Error, false, false, none);
        }
    }

    internal static ProbeResult Classify(HttpStatusCode code) => (int)code switch
    {
        404 or 410 or 451 => ProbeResult.Missing,
        401 or 403 => ProbeResult.Restricted,
        _ => ProbeResult.Error,
    };
}
