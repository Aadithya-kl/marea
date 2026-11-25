using Microsoft.Data.Sqlite;

namespace Marea.Core;

/// <summary>Durable provenance ledger (SQLite, WAL): files, verified sources, recipes, journal.</summary>
/// <remarks>Thread-safe: every call opens a pooled connection. Safe across processes (WAL).</remarks>
public sealed partial class Ledger
{
    private readonly string _cs;

    /// <summary>Opens (and migrates) the ledger at <paramref name="dbPath"/>.</summary>
    public Ledger(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = true }.ToString();
        using var c = Open();
        Exec(c, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS files(path TEXT PRIMARY KEY COLLATE NOCASE, root TEXT NOT NULL,
              size INTEGER, mtime INTEGER, sha TEXT, hashed_at INTEGER, class INTEGER, residency INTEGER,
              last_access INTEGER, evaporated_at INTEGER, source_label TEXT);
            CREATE INDEX IF NOT EXISTS ix_files_sha ON files(sha);
            CREATE TABLE IF NOT EXISTS sources(sha TEXT, kind INTEGER, locator TEXT, size INTEGER,
              verified_at INTEGER, PRIMARY KEY(sha, kind, locator));
            CREATE TABLE IF NOT EXISTS recipes(sha TEXT PRIMARY KEY, json TEXT NOT NULL, verified_at INTEGER);
            CREATE TABLE IF NOT EXISTS journal(id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER, action TEXT,
              path TEXT, bytes INTEGER, ok INTEGER, detail TEXT);
            CREATE TABLE IF NOT EXISTS meta(sha TEXT PRIMARY KEY, json TEXT NOT NULL, version INTEGER, at INTEGER);
            """);
        Migrate(c);
    }

    /// <summary>Adds columns introduced after 0.1.0 to existing ledgers.</summary>
    private static void Migrate(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('files') WHERE name='batch'";
        if ((long)cmd.ExecuteScalar()! == 0)
        {
            Exec(c, "ALTER TABLE files ADD COLUMN batch TEXT;");
        }
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        Exec(c, "PRAGMA busy_timeout=5000;");
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args)
        {
            cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        }
        cmd.ExecuteNonQuery();
    }

    /// <summary>Inserts or updates a file record.</summary>
    public void Upsert(FileRecord f)
    {
        using var c = Open();
        Exec(c, """
            INSERT INTO files(path, root, size, mtime, sha, hashed_at, class, residency, last_access, evaporated_at, source_label, batch)
            VALUES($p,$r,$s,$m,$h,$ha,$c,$res,$la,$ev,$sl,$b)
            ON CONFLICT(path) DO UPDATE SET root=$r, size=$s, mtime=$m, sha=$h, hashed_at=$ha, class=$c,
              residency=$res, last_access=$la, evaporated_at=$ev, source_label=$sl, batch=$b;
            """,
            ("$p", f.Path), ("$r", f.Root), ("$s", f.Size), ("$m", f.MTimeTicks), ("$h", f.Sha256),
            ("$ha", f.HashedUtc.Ticks), ("$c", (int)f.Class), ("$res", (int)f.Residency),
            ("$la", f.LastAccessUtc.Ticks), ("$ev", f.EvaporatedUtc?.Ticks), ("$sl", f.SourceLabel), ("$b", f.Batch));
    }

    /// <summary>Removes a file record (the file left the managed folders).</summary>
    public void Remove(string path)
    {
        using var c = Open();
        Exec(c, "DELETE FROM files WHERE path=$p", ("$p", path));
    }

    /// <summary>All tracked files.</summary>
    public List<FileRecord> Files() => QueryFiles($"SELECT {Columns} FROM files ORDER BY size DESC");

    private const string Columns = "path, root, size, mtime, sha, hashed_at, class, residency, last_access, evaporated_at, source_label, batch";

    /// <summary>The record at <paramref name="path"/>, if tracked.</summary>
    public FileRecord? Get(string path) => QueryFiles($"SELECT {Columns} FROM files WHERE path=$p", ("$p", path)).FirstOrDefault();

    /// <summary>All tracked files with the given content hash.</summary>
    public List<FileRecord> WithSha(string sha) => QueryFiles($"SELECT {Columns} FROM files WHERE sha=$h", ("$h", sha));

    private List<FileRecord> QueryFiles(string sql, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args)
        {
            cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        }
        using var r = cmd.ExecuteReader();
        var list = new List<FileRecord>();
        while (r.Read())
        {
            list.Add(new FileRecord
            {
                Path = r.GetString(0),
                Root = r.GetString(1),
                Size = r.GetInt64(2),
                MTimeTicks = r.GetInt64(3),
                Sha256 = r.IsDBNull(4) ? "" : r.GetString(4),
                HashedUtc = new DateTime(r.GetInt64(5), DateTimeKind.Utc),
                Class = (Recoverability)r.GetInt32(6),
                Residency = (Residency)r.GetInt32(7),
                LastAccessUtc = new DateTime(r.GetInt64(8), DateTimeKind.Utc),
                EvaporatedUtc = r.IsDBNull(9) ? null : new DateTime(r.GetInt64(9), DateTimeKind.Utc),
                SourceLabel = r.IsDBNull(10) ? "" : r.GetString(10),
                Batch = r.IsDBNull(11) ? null : r.GetString(11),
            });
        }
        return list;
    }

    /// <summary>Records (or refreshes) a verified source for a content hash.</summary>
    public void PutSource(Source s)
    {
        using var c = Open();
        Exec(c, """
            INSERT INTO sources(sha, kind, locator, size, verified_at) VALUES($h,$k,$l,$s,$v)
            ON CONFLICT(sha, kind, locator) DO UPDATE SET size=$s, verified_at=$v;
            """, ("$h", s.Sha256), ("$k", (int)s.Kind), ("$l", s.Locator), ("$s", s.Size), ("$v", s.VerifiedUtc.Ticks));
    }

    /// <summary>Forgets a source that turned out to be gone or wrong.</summary>
    public void DropSource(Source s)
    {
        using var c = Open();
        Exec(c, "DELETE FROM sources WHERE sha=$h AND kind=$k AND locator=$l",
            ("$h", s.Sha256), ("$k", (int)s.Kind), ("$l", s.Locator));
    }

    /// <summary>Known sources for a content hash.</summary>
    public List<Source> SourcesOf(string sha)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT kind, locator, size, verified_at FROM sources WHERE sha=$h";
        cmd.Parameters.AddWithValue("$h", sha);
        using var r = cmd.ExecuteReader();
        var list = new List<Source>();
        while (r.Read())
        {
            list.Add(new Source(sha, (SourceKind)r.GetInt32(0), r.GetString(1), r.GetInt64(2),
                new DateTime(r.GetInt64(3), DateTimeKind.Utc)));
        }
        return list;
    }

    /// <summary>Stores a verified recipe (JSON) for a target content hash.</summary>
    public void PutRecipe(string sha, string json)
    {
        using var c = Open();
        Exec(c, "INSERT INTO recipes(sha, json, verified_at) VALUES($h,$j,$v) ON CONFLICT(sha) DO UPDATE SET json=$j, verified_at=$v",
            ("$h", sha), ("$j", json), ("$v", DateTime.UtcNow.Ticks));
    }

    /// <summary>The recipe JSON for a content hash, if any.</summary>
    public string? GetRecipe(string sha)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json FROM recipes WHERE sha=$h";
        cmd.Parameters.AddWithValue("$h", sha);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Appends a journal line.</summary>
    public void Log(string action, string path, long bytes, bool ok, string detail = "")
    {
        using var c = Open();
        Exec(c, "INSERT INTO journal(at, action, path, bytes, ok, detail) VALUES($a,$ac,$p,$b,$o,$d)",
            ("$a", DateTime.UtcNow.Ticks), ("$ac", action), ("$p", path), ("$b", bytes), ("$o", ok ? 1 : 0), ("$d", detail));
    }

    /// <summary>Most recent journal lines, newest first.</summary>
    public List<JournalEntry> Journal(int limit = 500)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, at, action, path, bytes, ok, detail FROM journal ORDER BY id DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<JournalEntry>();
        while (r.Read())
        {
            list.Add(new JournalEntry(r.GetInt64(0), new DateTime(r.GetInt64(1), DateTimeKind.Utc), r.GetString(2),
                r.GetString(3), r.GetInt64(4), r.GetInt32(5) == 1, r.IsDBNull(6) ? "" : r.GetString(6)));
        }
        return list;
    }
}
