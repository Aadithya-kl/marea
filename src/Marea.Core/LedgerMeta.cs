namespace Marea.Core;

/// <summary>Model metadata, keyed by content hash so duplicates share it and it survives evaporation.</summary>
public sealed partial class Ledger
{
    /// <summary>Stores what the inspector learned about a content hash.</summary>
    public void PutMeta(string sha, ModelMeta meta)
    {
        using var c = Open();
        Exec(c, """
            INSERT INTO meta(sha, json, version, at) VALUES($h,$j,$v,$a)
            ON CONFLICT(sha) DO UPDATE SET json=$j, version=$v, at=$a
            """,
            ("$h", sha), ("$j", meta.ToJson()), ("$v", ModelMeta.InspectorVersion), ("$a", DateTime.UtcNow.Ticks));
    }

    /// <summary>When the current inspector last read this hash; null when it never did (or an older one did).</summary>
    public DateTime? MetaInspectedUtc(string sha)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT at FROM meta WHERE sha=$h AND version=$v";
        cmd.Parameters.AddWithValue("$h", sha);
        cmd.Parameters.AddWithValue("$v", ModelMeta.InspectorVersion);
        return cmd.ExecuteScalar() is long ticks ? new DateTime(ticks, DateTimeKind.Utc) : null;
    }

    /// <summary>Every stored metadata entry, by content hash.</summary>
    public Dictionary<string, ModelMeta> AllMeta()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT sha, json FROM meta";
        using var r = cmd.ExecuteReader();
        var all = new Dictionary<string, ModelMeta>(StringComparer.Ordinal);
        while (r.Read())
        {
            if (ModelMeta.FromJson(r.GetString(1)) is { } m)
            {
                all[r.GetString(0)] = m;
            }
        }
        return all;
    }
}
