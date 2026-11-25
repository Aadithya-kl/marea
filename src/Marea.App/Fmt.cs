using Marea.Core;

namespace Marea.App;

/// <summary>Formatting shared by the views, in the current language.</summary>
public static class Fmt
{
    /// <summary>Bytes as a short size: MB below 1 GB, otherwise GB with one decimal.</summary>
    public static string Gb(long bytes) =>
        bytes == 0 ? "0 GB" : bytes < 1L << 30 ? $"{Math.Max(1, bytes >> 20):N0} MB" : $"{bytes / (double)(1L << 30):N1} GB";

    /// <summary>A folder path for display, with the user profile shortened to "~" so screens and
    /// screenshots don't show the account name. Other paths are returned unchanged.</summary>
    public static string HomePath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        return home.Length > 0 && path.StartsWith(home, StringComparison.OrdinalIgnoreCase)
               && (path.Length == home.Length || path[home.Length] == '\\')
            ? "~" + path[home.Length..]
            : path;
    }

    /// <summary>Seconds as a short duration.</summary>
    public static string Duration(double s) =>
        double.IsInfinity(s) ? "—" : s < 1 ? Loc.T("dur.instant") : s < 90 ? $"{s:0} s" : s < 5400 ? $"{s / 60:0} min" : $"{s / 3600:0.0} h";

    /// <summary>Label of a recoverability class.</summary>
    public static string Class(Recoverability r) => Loc.T("class." + r.ToString().ToLowerInvariant());

    /// <summary>Label of a residency state.</summary>
    public static string Residency(Core.Residency r) => Loc.T("res." + r.ToString().ToLowerInvariant());

    /// <summary>Relative age.</summary>
    public static string Ago(DateTime utc)
    {
        if (utc.Year < 2000)
        {
            return "—";
        }
        var d = DateTime.UtcNow - utc;
        return d.TotalHours < 1 ? Loc.T("ago.now")
             : d.TotalDays < 1 ? Loc.T("ago.hours", Math.Floor(d.TotalHours))
             : d.TotalDays < 2 ? Loc.T("ago.yesterday")
             : Loc.T("ago.days", Math.Floor(d.TotalDays));
    }

    /// <summary>Icon resource key for a recoverability class.</summary>
    public static string ClassIcon(Recoverability c) => c switch
    {
        Recoverability.Duplicate => "Icon.Copy",
        Recoverability.Derivable => "Icon.Formula",
        Recoverability.Reobtainable => "Icon.CloudDown",
        _ => "Icon.Lock",
    };

    /// <summary>Icon and brush resource keys for a journal line.</summary>
    public static (string Icon, string Brush) JournalIcon(JournalEntry j) => j switch
    {
        { Ok: false } => ("Icon.Warning", "WarningBrush"),
        { Action: "j.evaporated" } => ("Icon.CloudUp", "EvaporatedBrush"),
        { Action: "j.returned_verified" or "j.brought_back" or "j.batch_undone" or "j.restore_all" } => ("Icon.CloudDown", "DuplicateBrush"),
        { Action: "j.recipe_verified" } => ("Icon.Formula", "DerivableBrush"),
        { Action: "j.kept" } => ("Icon.Shield", "UniqueBrush"),
        { Action: "j.root_added" or "j.root_removed" } => ("Icon.Folder", "UniqueBrush"),
        { Action: "j.replicated" } => ("Icon.Server", "ReobtainableBrush"),
        _ => ("Icon.Info", "UniqueBrush"),
    };

    /// <summary>A path shown relative to its managed folder ("models\sub\file"), so the UI and
    /// screenshots don't expose the user profile path. Paths outside managed folders are unchanged.</summary>
    public static string ShortPath(string path)
    {
        foreach (var root in App.Engine.Settings.ManagedRoots.OrderByDescending(r => r.Length))
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return System.IO.Path.GetFileName(root) + path[root.Length..];
            }
        }
        return path;
    }

    /// <summary>Renders a coded source label with managed-folder paths shortened.</summary>
    public static string Source(string coded)
    {
        var text = Loc.Render(coded);
        foreach (var root in App.Engine.Settings.ManagedRoots.OrderByDescending(r => r.Length))
        {
            text = text.Replace(root, System.IO.Path.GetFileName(root), StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    /// <summary>Folder relative to its managed root (e.g. "checkpoints › acestep-v15-turbo").</summary>
    public static string RelativeFolder(FileRecord f)
    {
        var dir = System.IO.Path.GetDirectoryName(f.Path) ?? "";
        var rel = System.IO.Path.GetRelativePath(f.Root, dir);
        var rootName = System.IO.Path.GetFileName(f.Root);
        return rel == "." ? rootName : rootName + " › " + rel.Replace("\\", " › ");
    }
}
