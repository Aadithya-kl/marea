namespace Marea.Core;

/// <summary>How a file's exact bytes can be obtained again if it leaves the disk.</summary>
public enum Recoverability
{
    /// <summary>No known source: never evaporated, only replicated.</summary>
    Unique = 0,
    /// <summary>Re-downloadable from a public hub or present on the mirror (NAS).</summary>
    Reobtainable = 1,
    /// <summary>Rebuildable bit-for-bit from other local files (e.g. bf16 from fp32).</summary>
    Derivable = 2,
    /// <summary>An identical copy is on this disk.</summary>
    Duplicate = 3,
}

/// <summary>Where the file's data lives right now.</summary>
public enum Residency
{
    /// <summary>Data on disk (ordinary file or hydrated placeholder).</summary>
    Local = 0,
    /// <summary>Placeholder with no data: comes back on first read.</summary>
    Evaporated = 1,
    /// <summary>File changed after it was hashed: treated as new content.</summary>
    Changed = 2,
    /// <summary>Path no longer exists.</summary>
    Missing = 3,
}

/// <summary>Kind of a stored source.</summary>
public enum SourceKind
{
    /// <summary>Public Hugging Face file pinned to a commit.</summary>
    HuggingFace = 1,
    /// <summary>Verified copy in the content-addressed mirror store.</summary>
    Mirror = 2,
    /// <summary>Verified recipe that rebuilds the bytes from local files.</summary>
    Recipe = 3,
}

/// <summary>A verified place the bytes of a content hash can be obtained from.</summary>
/// <param name="Sha256">Content hash (lower-case hex).</param>
/// <param name="Kind">Source kind.</param>
/// <param name="Locator">HF: <c>repo@commit:path</c>; mirror: absolute path; recipe: target sha.</param>
/// <param name="Size">Expected size in bytes.</param>
/// <param name="VerifiedUtc">Last time the source was confirmed to exist and match.</param>
public sealed record Source(string Sha256, SourceKind Kind, string Locator, long Size, DateTime VerifiedUtc);

/// <summary>One file tracked by the ledger.</summary>
public sealed class FileRecord
{
    /// <summary>Absolute path.</summary>
    public required string Path { get; init; }
    /// <summary>Managed folder that contains the file.</summary>
    public required string Root { get; init; }
    /// <summary>Logical size in bytes.</summary>
    public long Size { get; set; }
    /// <summary>Last write time (UTC ticks) when hashed.</summary>
    public long MTimeTicks { get; set; }
    /// <summary>Content sha256 (lower-case hex), empty until hashed.</summary>
    public string Sha256 { get; set; } = "";
    /// <summary>When the hash was computed.</summary>
    public DateTime HashedUtc { get; set; }
    /// <summary>Best recoverability found.</summary>
    public Recoverability Class { get; set; }
    /// <summary>Current residency.</summary>
    public Residency Residency { get; set; }
    /// <summary>Last observed use (hydration or file access time).</summary>
    public DateTime LastAccessUtc { get; set; }
    /// <summary>When it was evaporated, if it is.</summary>
    public DateTime? EvaporatedUtc { get; set; }
    /// <summary>Human readable description of the best source.</summary>
    public string SourceLabel { get; set; } = "";

    /// <summary>Size in GiB.</summary>
    public double SizeGb => Size / (double)(1L << 30);
}

/// <summary>One line of the append-only activity journal.</summary>
public sealed record JournalEntry(long Id, DateTime AtUtc, string Action, string Path, long Bytes, bool Ok, string Detail);
