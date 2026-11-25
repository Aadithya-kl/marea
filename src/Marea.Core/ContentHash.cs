using System.Buffers;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Marea.Core;

/// <summary>Streaming sha256 over files and handles.</summary>
public static class ContentHash
{
    private const int Chunk = 4 << 20;

    /// <summary>Hashes the whole content behind <paramref name="handle"/> (positional reads, no seek state).</summary>
    public static async Task<string> Sha256Async(SafeFileHandle handle, IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            long pos = 0;
            int n;
            while ((n = await RandomAccess.ReadAsync(handle, buf.AsMemory(0, Chunk), pos, ct).ConfigureAwait(false)) > 0)
            {
                h.AppendData(buf, 0, n);
                pos += n;
                progress?.Report(pos);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
        return Convert.ToHexStringLower(h.GetHashAndReset());
    }

    /// <summary>Hashes a file opened for shared reading.</summary>
    public static async Task<string> Sha256FileAsync(string path, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
        return await Sha256Async(handle, progress, ct).ConfigureAwait(false);
    }

    /// <summary>Encodes a content hash as the placeholder identity (32 raw bytes).</summary>
    public static byte[] ToIdentity(string sha) => Convert.FromHexString(sha);

    /// <summary>Decodes a placeholder identity back to the hex content hash.</summary>
    public static string FromIdentity(ReadOnlySpan<byte> identity) => Convert.ToHexStringLower(identity);
}
