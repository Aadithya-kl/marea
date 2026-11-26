namespace Marea.Core;

/// <summary>Decisions about programs that bring evaporated files back.</summary>
public sealed partial class MareaEngine
{
    /// <summary>Stops a program from bringing evaporated files back (its reads fail).</summary>
    public void BlockReader(string process)
    {
        Settings.AllowedReaders.RemoveAll(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase));
        if (!Guard.IsBlocked(process))
        {
            Settings.BlockedReaders.Add(process);
        }
        SaveSettings();
        Ledger.Log("j.reader_block_set", process, 0, true, ReaderGuard.DisplayName(process));
    }

    /// <summary>Lets a program read many evaporated files without alerts.</summary>
    public void AllowReader(string process)
    {
        Settings.BlockedReaders.RemoveAll(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase));
        if (!Settings.AllowedReaders.Contains(process, StringComparer.OrdinalIgnoreCase))
        {
            Settings.AllowedReaders.Add(process);
        }
        SaveSettings();
        Ledger.Log("j.reader_allowed", process, 0, true, ReaderGuard.DisplayName(process));
    }

    /// <summary>Removes a program from the blocked list.</summary>
    public void UnblockReader(string process)
    {
        Settings.BlockedReaders.RemoveAll(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase));
        SaveSettings();
        Ledger.Log("j.reader_unblocked", process, 0, true, ReaderGuard.DisplayName(process));
    }
}
