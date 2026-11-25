using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.CloudFilters;
using Windows.Win32.Storage.FileSystem;

namespace Marea.CloudFiles;

/// <summary>Observable on-disk state of a file under a sync root.</summary>
public enum PlaceholderState
{
    /// <summary>Ordinary file, not managed by the cloud filter.</summary>
    NormalFile,
    /// <summary>Placeholder whose data is fully on disk.</summary>
    Hydrated,
    /// <summary>Placeholder with no (or only partial) data on disk.</summary>
    Dehydrated,
    /// <summary>Placeholder that was modified and is no longer in sync.</summary>
    Modified,
}

/// <summary>Per-file Cloud Files operations: evaporate (convert + dehydrate), hydrate, revert, inspect.</summary>
public static class Placeholder
{
    /// <summary>Converts the file behind an <b>exclusive</b> handle into an in-sync placeholder and
    /// drops its data in the same call. <paramref name="identity"/> (max 4 KB) is handed back to the
    /// provider when the file has to be hydrated again.</summary>
    /// <param name="exclusiveHandle">Handle opened with read/write access and no sharing.</param>
    /// <param name="identity">Opaque identity bytes, e.g. the content sha256.</param>
    /// <exception cref="CloudFilesException">When conversion or dehydration fails.</exception>
    public static unsafe void ConvertAndDehydrate(SafeFileHandle exclusiveHandle, ReadOnlySpan<byte> identity)
    {
        if (identity.Length is 0 or > 4096)
        {
            throw new ArgumentException("L'identita' deve essere tra 1 e 4096 byte.", nameof(identity));
        }
        fixed (byte* pId = identity)
        {
            var hr = PInvoke.CfConvertToPlaceholder(
                new HANDLE(exclusiveHandle.DangerousGetHandle()), pId, (uint)identity.Length,
                CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC | CF_CONVERT_FLAGS.CF_CONVERT_FLAG_DEHYDRATE,
                null, null);
            CloudFilesException.ThrowIfFailed(hr, "l'evaporazione del file");
        }
    }

    /// <summary>Dehydrates a file that is already an in-sync placeholder (e.g. after a re-hydration).</summary>
    public static unsafe void Dehydrate(SafeFileHandle exclusiveHandle)
    {
        var hr = PInvoke.CfDehydratePlaceholder(new HANDLE(exclusiveHandle.DangerousGetHandle()), 0, -1,
            CF_DEHYDRATE_FLAGS.CF_DEHYDRATE_FLAG_NONE, null);
        CloudFilesException.ThrowIfFailed(hr, "l'evaporazione del file");
    }

    /// <summary>Hydrates the whole file now (blocks until the provider delivered all data).</summary>
    public static unsafe void Hydrate(string path)
    {
        using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hr = PInvoke.CfHydratePlaceholder(new HANDLE(h.DangerousGetHandle()), 0, -1,
            CF_HYDRATE_FLAGS.CF_HYDRATE_FLAG_NONE, null);
        CloudFilesException.ThrowIfFailed(hr, $"il ripristino di {path}");
    }

    /// <summary>Turns a fully hydrated placeholder back into an ordinary file.</summary>
    public static unsafe void Revert(string path)
    {
        using var h = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var hr = PInvoke.CfRevertPlaceholder(new HANDLE(h.DangerousGetHandle()),
            CF_REVERT_FLAGS.CF_REVERT_FLAG_NONE, null);
        CloudFilesException.ThrowIfFailed(hr, $"la conversione in file normale di {path}");
    }

    /// <summary>Reads the placeholder state from attributes and reparse tag without touching data.</summary>
    public static unsafe PlaceholderState GetState(string path)
    {
        // Attribute-only handle: opening with data access could trigger a hydration.
        using var h = Native.OpenAttributesOnly(path);
        FILE_ATTRIBUTE_TAG_INFO tag;
        if (h.IsInvalid || !PInvoke.GetFileInformationByHandleEx(new HANDLE(h.DangerousGetHandle()),
                FILE_INFO_BY_HANDLE_CLASS.FileAttributeTagInfo, &tag, (uint)sizeof(FILE_ATTRIBUTE_TAG_INFO)))
        {
            return PlaceholderState.NormalFile;
        }
        var state = PInvoke.CfGetPlaceholderStateFromAttributeTag(tag.FileAttributes, tag.ReparseTag);
        if (state == CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_INVALID
            || !state.HasFlag(CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PLACEHOLDER))
        {
            return PlaceholderState.NormalFile;
        }
        // The attribute-tag info does not always carry the recall bits: read them from the path.
        const int recallOnDataAccess = 0x00400000;
        const int offline = 0x00001000;
        var attrs = (int)File.GetAttributes(path);
        if ((attrs & (recallOnDataAccess | offline)) != 0 || state.HasFlag(CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL))
        {
            return PlaceholderState.Dehydrated;
        }
        return state.HasFlag(CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC) ? PlaceholderState.Hydrated : PlaceholderState.Modified;
    }

    /// <summary>Bytes the file really occupies on disk (0 for a dehydrated placeholder).</summary>
    public static long SizeOnDisk(string path)
    {
        var low = Native.GetCompressedFileSizeW(path, out var high);
        return ((long)high << 32) | low;
    }

    private static class Native
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa,
            uint disposition, uint flags, IntPtr template);

        /// <summary>Opens a handle with FILE_READ_ATTRIBUTES only, never recalling cloud data.</summary>
        public static SafeFileHandle OpenAttributesOnly(string path)
        {
            const uint fileReadAttributes = 0x80, shareAll = 7, openExisting = 3;
            const uint backupSemantics = 0x02000000, openReparsePoint = 0x00200000, openNoRecall = 0x00100000;
            return CreateFileW(path, fileReadAttributes, shareAll, IntPtr.Zero, openExisting,
                backupSemantics | openReparsePoint | openNoRecall, IntPtr.Zero);
        }
    }
}
