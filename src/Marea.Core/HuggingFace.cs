using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marea.Core;

/// <summary>A public LFS file on the Hub, pinned to a commit.</summary>
/// <param name="Repo">owner/name.</param>
/// <param name="Commit">Commit sha the file is pinned to.</param>
/// <param name="Path">Path inside the repository.</param>
/// <param name="Sha256">LFS object id (sha256 of the content).</param>
/// <param name="Size">Size in bytes.</param>
public sealed record HubFile(string Repo, string Commit, string Path, string Sha256, long Size)
{
    /// <summary>Ledger locator: <c>repo@commit:path</c>.</summary>
    public string Locator => $"{Repo}@{Commit}:{Path}";

    /// <summary>Parses a locator produced by <see cref="Locator"/>.</summary>
    public static HubFile Parse(string locator, string sha, long size)
    {
        var at = locator.IndexOf('@');
        var colon = locator.IndexOf(':', at);
        return new HubFile(locator[..at], locator[(at + 1)..colon], locator[(colon + 1)..], sha, size);
    }
}

/// <summary>Minimal, read-only Hugging Face Hub client. Sends no credentials and no local data:
/// only public repository names. Every request and redirect must stay on Hub hosts over HTTPS.</summary>
public sealed partial class HuggingFaceClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Creates the client (redirects are followed manually to enforce the host allow-list).</summary>
    public HuggingFaceClient(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Marea", "0.1"));
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-]*/[A-Za-z0-9_.\-]+$")]
    private static partial Regex RepoName();

    /// <summary>True for HTTPS URLs on huggingface.co or hf.co (and their subdomains).</summary>
    public static bool IsAllowedHost(Uri u) =>
        u.Scheme == Uri.UriSchemeHttps &&
        (u.Host == "huggingface.co" || u.Host.EndsWith(".huggingface.co", StringComparison.Ordinal)
         || u.Host == "hf.co" || u.Host.EndsWith(".hf.co", StringComparison.Ordinal));

    /// <summary>Lists every LFS file of <paramref name="repo"/> at its current main commit.</summary>
    /// <exception cref="ArgumentException">For an invalid repository name.</exception>
    public async Task<List<HubFile>> ListLfsFilesAsync(string repo, CancellationToken ct = default)
    {
        if (!RepoName().IsMatch(repo))
        {
            throw new ArgumentException(Loc.T("hub.bad_repo", repo));
        }
        using var info = await SendAsync(HttpMethod.Get, new Uri($"https://huggingface.co/api/models/{repo}"), null, ct);
        info.EnsureSuccessStatusCode();
        using var infoDoc = JsonDocument.Parse(await info.Content.ReadAsStringAsync(ct));
        var commit = infoDoc.RootElement.GetProperty("sha").GetString()!;
        var files = new List<HubFile>();
        Uri? next = new($"https://huggingface.co/api/models/{repo}/tree/{commit}?recursive=true");
        while (next is not null)
        {
            using var resp = await SendAsync(HttpMethod.Get, next, null, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object)
                {
                    files.Add(new HubFile(repo, commit, e.GetProperty("path").GetString()!,
                        lfs.GetProperty("oid").GetString()!, lfs.GetProperty("size").GetInt64()));
                }
            }
            next = NextLink(resp);
        }
        return files;
    }

    /// <summary>Confirms the pinned file still exists with the expected hash and size.</summary>
    public async Task<bool> VerifyAsync(HubFile f, CancellationToken ct = default)
    {
        using var resp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, ResolveUri(f)), ct);
        if (resp.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.OK))
        {
            return false;
        }
        var etag = Header(resp, "X-Linked-ETag")?.Trim('"');
        var size = Header(resp, "X-Linked-Size");
        return string.Equals(etag, f.Sha256, StringComparison.OrdinalIgnoreCase) && size == f.Size.ToString();
    }

    /// <summary>Opens the byte range [from, to] of the pinned file.</summary>
    public async Task<Stream> OpenRangeAsync(HubFile f, long from, long to, CancellationToken ct)
    {
        var resp = await SendAsync(HttpMethod.Get, ResolveUri(f), new RangeHeaderValue(from, to), ct);
        if (resp.StatusCode != HttpStatusCode.PartialContent && !(from == 0 && resp.StatusCode == HttpStatusCode.OK))
        {
            resp.Dispose();
            throw new HttpRequestException(Loc.T("hub.status", (int)resp.StatusCode, f.Path));
        }
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    private static Uri ResolveUri(HubFile f)
    {
        var path = string.Join('/', f.Path.Split('/').Select(Uri.EscapeDataString));
        return new Uri($"https://huggingface.co/{f.Repo}/resolve/{f.Commit}/{path}");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, RangeHeaderValue? range, CancellationToken ct)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            if (!IsAllowedHost(uri))
            {
                throw new HttpRequestException(Loc.T("hub.host_denied", uri.Host));
            }
            var req = new HttpRequestMessage(method, uri);
            if (range is not null)
            {
                req.Headers.Range = range;
            }
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
            {
                uri = loc.IsAbsoluteUri ? loc : new Uri(uri, loc);
                resp.Dispose();
                continue;
            }
            return resp;
        }
        throw new HttpRequestException(Loc.T("hub.redirects"));
    }

    private static string? Header(HttpResponseMessage r, string name) =>
        r.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;

    private static Uri? NextLink(HttpResponseMessage r)
    {
        if (!r.Headers.TryGetValues("Link", out var links))
        {
            return null;
        }
        foreach (var part in string.Join(",", links).Split(','))
        {
            if (part.Contains("rel=\"next\""))
            {
                var s = part.IndexOf('<') + 1;
                var e = part.IndexOf('>');
                return new Uri(part[s..e]);
            }
        }
        return null;
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}

/// <summary>Range source over a pinned Hub file; reuses one HTTP stream for sequential reads.</summary>
public sealed class HubRangeSource(HuggingFaceClient client, HubFile file) : IRangeSource
{
    private Stream? _stream;
    private long _pos = -1;

    /// <inheritdoc/>
    public long Length => file.Size;

    /// <inheritdoc/>
    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken ct)
    {
        if (offset >= file.Size)
        {
            return 0;
        }
        if (_stream is null || _pos != offset)
        {
            if (_stream is not null)
            {
                await _stream.DisposeAsync();
            }
            _stream = await client.OpenRangeAsync(file, offset, file.Size - 1, ct);
            _pos = offset;
        }
        var n = await _stream.ReadAsync(buffer, ct);
        _pos += n;
        return n;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }
    }
}
