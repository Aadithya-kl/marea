using Microsoft.Win32;

namespace Marea.App;

/// <summary>Per-user "start with Windows" entry (HKCU Run). Written only when the user turns it on.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Marea";

    /// <summary>True when the entry exists and points at this executable.</summary>
    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) is string v && v.Contains(Environment.ProcessPath!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds or removes the entry.</summary>
    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            k.SetValue(Name, $"\"{Environment.ProcessPath}\" --tray");
        }
        else if (k.GetValue(Name) is not null)
        {
            k.DeleteValue(Name);
        }
    }
}
