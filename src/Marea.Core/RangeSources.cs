namespace Marea.Core;

/// <summary>Random-access reader over the bytes of one content, wherever they come from.</summary>
public interface IRangeSource : IAsyncDisposable
{
    /// <summary>Total length of the content.</summary>
    long Length { get; }

    /// <summary>Reads up to <paramref name="buffer"/>.Length bytes at <paramref name="offset"/>; 0 at end.</summary>
    ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken ct);
}

/// <summary>Reads from a local or network file (duplicate copy or mirror object).</summary>
public sealed class FileRangeSource : IRangeSource
{
    private readonly Microsoft.Win32.SafeHandles.SafeFileHandle _h;

    /// <summary>Opens <paramref name="path"/> for shared reading.</summary>
    public FileRangeSource(string path)
    {
        _h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
        Length = RandomAccess.GetLength(_h);
    }

    /// <inheritdoc/>
    public long Length { get; }

    /// <inheritdoc/>
    public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken ct) =>
        RandomAccess.ReadAsync(_h, buffer, offset, ct);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _h.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Helpers to read exact ranges from any source.</summary>
public static class RangeSourceExtensions
{
    /// <summary>Fills <paramref name="buffer"/> completely unless the content ends first.</summary>
    public static async ValueTask<int> ReadFullyAsync(this IRangeSource s, long offset, Memory<byte> buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await s.ReadAsync(offset + total, buffer[total..], ct).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }
            total += n;
        }
        return total;
    }
}
