using Marea.CloudFiles;

namespace Marea.Core;

/// <summary>Progress of a scan.</summary>
/// <param name="Phase">What is happening.</param>
/// <param name="Path">Current file.</param>
/// <param name="Done">Bytes processed.</param>
/// <param name="Total">Bytes to process.</param>
public sealed record ScanProgress(string Phase, string Path, long Done, long Total);

/// <summary>Walks managed folders and keeps the ledger in step with the disk. Hashes only what changed.</summary>
public sealed class Scanner(Ledger ledger, MareaSettings settings)
{
    /// <summary>Scans every managed folder; returns the number of files hashed.</summary>
    public async Task<int> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var minSize = settings.MinFileSizeMb << 20;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var work = new List<(string Root, FileInfo File)>();
        foreach (var root in settings.ManagedRoots.Where(Directory.Exists))
        {
            foreach (var fi in Walk(new DirectoryInfo(root)))
            {
                if (fi.Length >= minSize)
                {
                    work.Add((root, fi));
                    seen.Add(fi.FullName);
                }
            }
        }
        var total = work.Sum(w => w.File.Length);
        long done = 0;
        var hashed = 0;
        foreach (var (root, fi) in work)
        {
            ct.ThrowIfCancellationRequested();
            if (await RefreshAsync(root, fi, total, done, progress, ct))
            {
                hashed++;
            }
            done += fi.Length;
        }
        foreach (var gone in ledger.Files().Where(f => !seen.Contains(f.Path)))
        {
            ledger.Remove(gone.Path);
            ledger.Log("j.removed", gone.Path, gone.Size, true);
        }
        return hashed;
    }

    /// <summary>Updates one file; returns true when it had to be hashed.</summary>
    private async Task<bool> RefreshAsync(string root, FileInfo fi, long total, long done,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var rec = ledger.Get(fi.FullName);
        var state = Placeholder.GetState(fi.FullName);
        if (state == PlaceholderState.Dehydrated)
        {
            if (rec is not null && rec.Residency != Residency.Evaporated)
            {
                rec.Residency = Residency.Evaporated;
                ledger.Upsert(rec);
            }
            return false;
        }
        if (state == PlaceholderState.Modified)
        {
            Placeholder.Revert(fi.FullName); // edited by the user: it is ordinary content again
            ledger.Log("j.user_modified", fi.FullName, fi.Length, true);
            fi.Refresh();
            rec = null;
        }
        var mtime = fi.LastWriteTimeUtc.Ticks;
        if (rec is not null && rec.Size == fi.Length && rec.MTimeTicks == mtime && rec.Sha256.Length == 64)
        {
            rec.Residency = Residency.Local;
            rec.LastAccessUtc = ObservedAccess(fi, rec);
            ledger.Upsert(rec);
            return false;
        }
        var accessBefore = fi.LastAccessTimeUtc;
        var hashProgress = new Progress<long>(b => progress?.Report(new ScanProgress(Loc.T("phase.hash"), fi.FullName, done + b, total)));
        var sha = await ContentHash.Sha256FileAsync(fi.FullName, hashProgress, ct);
        ledger.Upsert(new FileRecord
        {
            Path = fi.FullName,
            Root = root,
            Size = fi.Length,
            MTimeTicks = mtime,
            Sha256 = sha,
            HashedUtc = DateTime.UtcNow,
            Class = Recoverability.Unique,
            Residency = Residency.Local,
            LastAccessUtc = Max(accessBefore, fi.LastWriteTimeUtc),
        });
        return true;
    }

    /// <summary>Access time seen on disk, ignoring the read Marea itself did while hashing.</summary>
    private static DateTime ObservedAccess(FileInfo fi, FileRecord rec)
    {
        var atime = fi.LastAccessTimeUtc;
        var ourRead = atime >= rec.HashedUtc.AddHours(-1) && atime <= rec.HashedUtc.AddHours(2);
        return ourRead ? rec.LastAccessUtc : Max(rec.LastAccessUtc, atime);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    /// <summary>Recursive walk that never follows junctions or symbolic links.</summary>
    private static IEnumerable<FileInfo> Walk(DirectoryInfo dir)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(dir);
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = d.EnumerateFileSystemInfos("*", opts).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var e in entries)
            {
                if (e is DirectoryInfo sub)
                {
                    if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        stack.Push(sub);
                    }
                }
                else if (e is FileInfo f)
                {
                    yield return f;
                }
            }
        }
    }
}
