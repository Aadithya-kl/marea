using System.Net.Http.Headers;

namespace Marea.Core;

/// <summary>HTTP with a host allow-list enforced on the first request and on every redirect.
/// Redirects are followed by hand (auto-redirect is off), HTTPS only, no cookies, no credentials.</summary>
public static class SafeHttp
{
    /// <summary>Creates a client that never follows redirects on its own.</summary>
    public static HttpClient CreateClient()
    {
        var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Marea", typeof(SafeHttp).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return http;
    }

    /// <summary>Sends a request and follows up to 5 redirects, each checked against <paramref name="allowed"/>.</summary>
    /// <exception cref="HttpRequestException">When a hop leaves the allow-list or redirects loop.</exception>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, Uri uri,
        RangeHeaderValue? range, Func<Uri, bool> allowed, CancellationToken ct, bool followRedirects = true)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !allowed(uri))
            {
                throw new HttpRequestException(Loc.T("hub.host_denied", uri.Host));
            }
            var req = new HttpRequestMessage(method, uri);
            if (range is not null)
            {
                req.Headers.Range = range;
            }
            var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (followRedirects && (int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
            {
                uri = loc.IsAbsoluteUri ? loc : new Uri(uri, loc);
                resp.Dispose();
                continue;
            }
            return resp;
        }
        throw new HttpRequestException(Loc.T("hub.redirects"));
    }
}

/// <summary>Range source over any HTTP URL; reuses one response stream for sequential reads.</summary>
/// <param name="open">Opens the byte range [from, end] and returns its stream.</param>
/// <param name="length">Total content length.</param>
public sealed class HttpRangeSource(Func<long, CancellationToken, Task<Stream>> open, long length) : IRangeSource
{
    private Stream? _stream;
    private long _pos = -1;

    /// <inheritdoc/>
    public long Length => length;

    /// <inheritdoc/>
    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken ct)
    {
        if (offset >= length)
        {
            return 0;
        }
        if (_stream is null || _pos != offset)
        {
            if (_stream is not null)
            {
                await _stream.DisposeAsync();
            }
            _stream = await open(offset, ct);
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
