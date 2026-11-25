using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.CloudFilters;

namespace Marea.CloudFiles;

/// <summary>A request from Windows to deliver the data of a dehydrated file.</summary>
public sealed class FetchRequest
{
    private readonly CF_CONNECTION_KEY _connection;
    private readonly long _transferKey;
    private readonly long _requestKey;

    internal FetchRequest(CF_CONNECTION_KEY connection, long transferKey, long requestKey, byte[] identity,
        string path, long fileSize, long offset, long length, long optionalOffset, long optionalLength)
    {
        _connection = connection;
        _transferKey = transferKey;
        _requestKey = requestKey;
        Identity = identity;
        Path = path;
        FileSize = fileSize;
        RequiredOffset = offset;
        RequiredLength = length;
        OptionalFileOffset = optionalOffset;
        OptionalLength = optionalLength;
    }

    /// <summary>Extra range Windows would like (progressive hydration), may be 0.</summary>
    public long OptionalFileOffset { get; }
    /// <summary>Length of the optional range.</summary>
    public long OptionalLength { get; }

    /// <summary>Identity bytes stored when the file was evaporated.</summary>
    public byte[] Identity { get; }
    /// <summary>Volume-relative path of the file (informational).</summary>
    public string Path { get; }
    /// <summary>Logical size of the file.</summary>
    public long FileSize { get; }
    /// <summary>First byte Windows needs.</summary>
    public long RequiredOffset { get; }
    /// <summary>Number of bytes Windows needs.</summary>
    public long RequiredLength { get; }
    /// <summary>Cancelled when Windows withdraws the request (e.g. the reading app gave up).</summary>
    public CancellationToken Cancellation => _cts.Token;
    internal readonly CancellationTokenSource _cts = new();
    internal long TransferKey => _transferKey;

    /// <summary>Delivers a block of data. Offsets and lengths must be 4 KB aligned except at end of file.</summary>
    public unsafe void Transfer(long offset, ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
        {
            Execute(p, offset, data.Length, new NTSTATUS(0));
        }
    }

    /// <summary>Reports progress to Windows (shown in Explorer) and keeps the request alive.</summary>
    public void ReportProgress(long total, long completed)
    {
        _ = PInvoke.CfReportProviderProgress(_connection, _transferKey, total, completed);
    }

    /// <summary>Fails the request: the reading application receives an I/O error, no data is written.</summary>
    public unsafe void Fail(long offset, long length)
    {
        const int statusCloudFileUnsuccessful = unchecked((int)0xC000CF16);
        Execute(null, offset, length, new NTSTATUS(statusCloudFileUnsuccessful));
    }

    private unsafe void Execute(byte* buffer, long offset, long length, NTSTATUS status)
    {
        var info = new CF_OPERATION_INFO
        {
            StructSize = (uint)sizeof(CF_OPERATION_INFO),
            Type = CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA,
            ConnectionKey = _connection,
            TransferKey = _transferKey,
            RequestKey = _requestKey,
        };
        var op = new CF_OPERATION_PARAMETERS();
        op.ParamSize = (uint)((byte*)Unsafe.AsPointer(ref op.Anonymous.TransferData) - (byte*)&op)
                       + (uint)sizeof(CF_OPERATION_PARAMETERS._Anonymous_e__Union._TransferData_e__Struct);
        op.TransferData.CompletionStatus = status;
        op.TransferData.Buffer = buffer;
        op.TransferData.Offset = offset;
        op.TransferData.Length = length;
        CloudFilesException.ThrowIfFailed(PInvoke.CfExecute(&info, &op), "il trasferimento dei dati");
    }
}

/// <summary>Live connection between Marea and one sync root: receives hydration requests.</summary>
public sealed class ProviderConnection : IDisposable
{
    private static readonly ConcurrentDictionary<long, ProviderConnection> Connections = new();
    private readonly ConcurrentDictionary<long, FetchRequest> _inflight = new();
    private readonly Func<FetchRequest, Task> _handler;
    private readonly unsafe CF_CALLBACK_REGISTRATION* _table;
    private CF_CONNECTION_KEY _key;
    private bool _disposed;

    /// <summary>Root this connection serves.</summary>
    public string Root { get; }

    private unsafe ProviderConnection(string root, Func<FetchRequest, Task> handler)
    {
        Root = root;
        _handler = handler;
        _table = (CF_CALLBACK_REGISTRATION*)NativeMemory.AllocZeroed(3, (nuint)sizeof(CF_CALLBACK_REGISTRATION));
        _table[0].Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA;
        _table[0].Callback = &OnFetchData;
        _table[1].Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA;
        _table[1].Callback = &OnCancelFetchData;
        _table[2].Type = (CF_CALLBACK_TYPE)unchecked((int)0xFFFFFFFF); // CF_CALLBACK_TYPE_NONE terminator
    }

    /// <summary>Connects to a registered sync root. <paramref name="handler"/> runs on the thread pool
    /// for every hydration request and must deliver or fail it.</summary>
    public static unsafe ProviderConnection Connect(string root, Func<FetchRequest, Task> handler)
    {
        var c = new ProviderConnection(root, handler);
        CF_CONNECTION_KEY key;
        fixed (char* p = root)
        {
            var hr = PInvoke.CfConnectSyncRoot(p, c._table, null,
                CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH, &key);
            if (hr.Failed)
            {
                NativeMemory.Free(c._table);
                CloudFilesException.ThrowIfFailed(hr, $"la connessione alla cartella {root}");
            }
        }
        c._key = key;
        Connections[key.Value] = c;
        return c;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void OnFetchData(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* p)
    {
        if (!Connections.TryGetValue(info->ConnectionKey.Value, out var c))
        {
            return;
        }
        var identity = new ReadOnlySpan<byte>(info->FileIdentity, (int)info->FileIdentityLength).ToArray();
        var path = info->NormalizedPath.ToString();
        var req = new FetchRequest(info->ConnectionKey, info->TransferKey, info->RequestKey, identity, path,
            info->FileSize, p->FetchData.RequiredFileOffset, p->FetchData.RequiredLength,
            p->FetchData.OptionalFileOffset, p->FetchData.OptionalLength);
        c._inflight[req.TransferKey] = req;
        _ = Task.Run(() => c.ServeAsync(req));
    }

    /// <summary>Runs the handler; any exception fails the request so the reader never hangs.</summary>
    private async Task ServeAsync(FetchRequest req)
    {
        try
        {
            await _handler(req).ConfigureAwait(false);
        }
        catch (Exception)
        {
            try { req.Fail(req.RequiredOffset, req.RequiredLength); } catch (CloudFilesException) { }
        }
        finally
        {
            _inflight.TryRemove(req.TransferKey, out _);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void OnCancelFetchData(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* p)
    {
        if (Connections.TryGetValue(info->ConnectionKey.Value, out var c)
            && c._inflight.TryGetValue(info->TransferKey, out var req))
        {
            req._cts.Cancel();
        }
    }

    /// <summary>Disconnects: dehydrated files can no longer be opened until the next connection.</summary>
    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Connections.TryRemove(_key.Value, out _);
        _ = PInvoke.CfDisconnectSyncRoot(_key);
        NativeMemory.Free(_table);
    }
}
