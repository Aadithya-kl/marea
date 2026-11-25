using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Marea.Core;

/// <summary>Small, read-only facts about files and folders needed by the safety rules.</summary>
public static class FileSystemFacts
{
    /// <summary>Number of hard links pointing at the file (1 for an ordinary file).</summary>
    public static int HardLinkCount(SafeFileHandle h) =>
        GetFileInformationByHandle(h, out var info) ? (int)info.NumberOfLinks : 1;

    /// <summary>Why a folder cannot be managed, or null when it can.</summary>
    public static string? RejectRoot(string path, IEnumerable<string> existingRoots)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\');
        if (!Directory.Exists(full))
        {
            return Loc.T("root.missing");
        }
        var drive = new DriveInfo(Path.GetPathRoot(full)!);
        if (drive.DriveType != DriveType.Fixed)
        {
            return Loc.T("root.not_fixed");
        }
        if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            return Loc.T("root.not_ntfs");
        }
        if (full.Length <= 3)
        {
            return Loc.T("root.whole_drive");
        }
        // System trees: never inside. Broad personal folders: never the folder itself.
        string[] systemTrees =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ];
        string[] broadFolders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ];
        bool Same(string a, string b) => string.Equals(a, b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        if (systemTrees.Any(t => t.Length > 0 && (Same(full, t) || IsUnder(full, t)))
            || broadFolders.Any(b => b.Length > 0 && Same(full, b)))
        {
            return Loc.T("root.system");
        }
        if (CloudFiles.SyncRoot.IsForeignRoot(full))
        {
            return Loc.T("root.foreign");
        }
        foreach (var r in existingRoots)
        {
            var rr = Path.GetFullPath(r).TrimEnd('\\');
            if (string.Equals(rr, full, StringComparison.OrdinalIgnoreCase))
            {
                return Loc.T("root.already");
            }
            if (IsUnder(full, rr) || IsUnder(rr, full))
            {
                return Loc.T("root.overlap", rr);
            }
        }
        return null;
    }

    /// <summary>True when <paramref name="child"/> is strictly inside <paramref name="parent"/>.</summary>
    public static bool IsUnder(string child, string parent) =>
        child.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Free bytes on the volume holding <paramref name="path"/>.</summary>
    public static long FreeBytes(string path) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;

    /// <summary>Total bytes of the volume holding <paramref name="path"/>.</summary>
    public static long TotalBytes(string path) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).TotalSize;

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle h, out ByHandleFileInformation info);
}
