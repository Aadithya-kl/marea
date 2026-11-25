using System.Text.Json;

namespace Marea.Core;

/// <summary>A newer release available on GitHub.</summary>
/// <param name="Version">Release version.</param>
/// <param name="Url">Release page (always on github.com).</param>
public sealed record UpdateInfo(Version Version, Uri Url);

/// <summary>Asks GitHub whether a newer Marea release exists. Notification only: it never downloads
/// or installs anything. One HTTPS request to api.github.com; no data about the user is sent.</summary>
public static class UpdateChecker
{
    /// <summary>Public repository the releases come from.</summary>
    public const string Repository = "indecenti/marea";

    /// <summary>The releases page shown to the user.</summary>
    public static readonly Uri ReleasesPage = new($"https://github.com/{Repository}/releases");

    /// <summary>Version of the running build.</summary>
    public static Version Current => typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Returns the newest release when it is newer than <paramref name="current"/>, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var http = SafeHttp.CreateClient();
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await SafeHttp.SendAsync(http, HttpMethod.Get,
            new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=10"), null,
            u => u.Host == "api.github.com", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return Newest(doc.RootElement, current);
    }

    /// <summary>Picks the highest non-draft release newer than <paramref name="current"/> (pure, testable).</summary>
    public static UpdateInfo? Newest(JsonElement releases, Version current)
    {
        UpdateInfo? best = null;
        foreach (var r in releases.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.GetBoolean())
            {
                continue;
            }
            var tag = r.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
            var core = tag.Split('-', '+')[0];
            if (!Version.TryParse(core, out var v) || Normalize(v) <= Normalize(current))
            {
                continue;
            }
            if (best is null || Normalize(v) > Normalize(best.Version))
            {
                // Only ever point at the official releases page of this repository.
                best = new UpdateInfo(v, new Uri($"{ReleasesPage}/tag/v{core}"));
            }
        }
        return best;
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
