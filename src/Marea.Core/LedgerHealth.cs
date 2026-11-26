namespace Marea.Core;

/// <summary>Health of public sources, as last seen by the source watch.</summary>
public sealed partial class Ledger
{
    /// <summary>Stores a health result; keeps the start time while the state does not change, and
    /// keeps a conclusive state when the new check could not conclude (unreachable).</summary>
    /// <returns>The stored status and whether the state changed.</returns>
    public (SourceStatus Status, bool Changed) PutHealth(string locator, SourceHealth state, string detail, DateTime nowUtc)
    {
        var old = Health(locator);
        if (state == SourceHealth.Unreachable && old is { State: not (SourceHealth.Unknown or SourceHealth.Unreachable) })
        {
            state = old.State;
            detail = old.Detail;
        }
        var since = old is not null && old.State == state ? old.SinceUtc : nowUtc;
        using var c = Open();
        Exec(c, """
            INSERT INTO source_health(locator, state, detail, checked, since) VALUES($l,$s,$d,$c,$n)
            ON CONFLICT(locator) DO UPDATE SET state=$s, detail=$d, checked=$c, since=$n
            """,
            ("$l", locator), ("$s", (int)state), ("$d", detail), ("$c", nowUtc.Ticks), ("$n", since.Ticks));
        return (new SourceStatus(locator, state, detail, nowUtc, since), old is null ? state != SourceHealth.Unknown : old.State != state);
    }

    /// <summary>Last known health of a source; null when never checked.</summary>
    public SourceStatus? Health(string locator)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT state, detail, checked, since FROM source_health WHERE locator=$l";
        cmd.Parameters.AddWithValue("$l", locator);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new SourceStatus(locator, (SourceHealth)r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1),
                new DateTime(r.GetInt64(2), DateTimeKind.Utc), new DateTime(r.GetInt64(3), DateTimeKind.Utc))
            : null;
    }

    /// <summary>Stores what Hugging Face's own security scanners say about a file (clean, caution, unsafe...).</summary>
    public void PutHubScan(string sha, string level, string repo)
    {
        using var c = Open();
        Exec(c, "INSERT INTO hub_scan(sha, level, repo, at) VALUES($h,$l,$r,$a) ON CONFLICT(sha) DO UPDATE SET level=$l, repo=$r, at=$a",
            ("$h", sha), ("$l", level), ("$r", repo), ("$a", DateTime.UtcNow.Ticks));
    }

    /// <summary>Hugging Face scan level by content hash.</summary>
    public Dictionary<string, string> HubScans()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT sha, level FROM hub_scan";
        using var r = cmd.ExecuteReader();
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        while (r.Read())
        {
            all[r.GetString(0)] = r.GetString(1);
        }
        return all;
    }

    /// <summary>Health state of a source, <see cref="SourceHealth.Unknown"/> when never checked.</summary>
    public SourceHealth HealthState(string locator) => Health(locator)?.State ?? SourceHealth.Unknown;
}
