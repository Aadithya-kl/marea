using System.Buffers;
using System.Security.Cryptography;

namespace Marea.Core;

/// <summary>Content-addressed copy store on a mirror folder (typically a NAS):
/// <c>{root}\marea-store\sha256\{aa}\{sha}</c>. Identical contents are stored once.</summary>
public sealed class MirrorStore(string root)
{
    /// <summary>Path of the object for a content hash.</summary>
    public string ObjectPath(string sha) => Path.Combine(root, "marea-store", "sha256", sha[..2], sha);

    /// <summary>True when the object exists with the expected size.</summary>
    public bool Has(string sha, long size)
    {
        try
        {
            var fi = new FileInfo(ObjectPath(sha));
            return fi.Exists && fi.Length == size;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Copies a file into the store, hashing while copying; the object only appears
    /// (atomic rename) when the copy hashes to <paramref name="sha"/>.</summary>
    /// <exception cref="InvalidDataException">When the copied bytes do not match.</exception>
    public async Task<Source> ReplicateAsync(string path, string sha, IProgress<long>? progress, CancellationToken ct)
    {
        var dest = ObjectPath(sha);
        var size = new FileInfo(path).Length;
        if (!Has(sha, size))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var tmp = dest + ".part";
            using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = ArrayPool<byte>.Shared.Rent(8 << 20);
            try
            {
                await using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, true))
                await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1, true))
                {
                    long done = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf.AsMemory(0, 8 << 20), ct)) > 0)
                    {
                        h.AppendData(buf, 0, n);
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                        done += n;
                        progress?.Report(done);
                    }
                }
                if (Convert.ToHexStringLower(h.GetHashAndReset()) != sha)
                {
                    File.Delete(tmp);
                    throw new InvalidDataException(Loc.T("mirror.mismatch"));
                }
                File.Move(tmp, dest, overwrite: true);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }
        return new Source(sha, SourceKind.Mirror, dest, size, DateTime.UtcNow);
    }
}
