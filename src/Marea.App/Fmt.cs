using Marea.Core;

namespace Marea.App;

/// <summary>Formatting shared by the views, in the current language.</summary>
public static class Fmt
{
    /// <summary>Bytes as GB with one decimal.</summary>
    public static string Gb(long bytes) => $"{bytes / (double)(1L << 30):N1} GB";

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

    /// <summary>Folder relative to its managed root (e.g. "checkpoints › acestep-v15-turbo").</summary>
    public static string RelativeFolder(FileRecord f)
    {
        var dir = System.IO.Path.GetDirectoryName(f.Path) ?? "";
        var rel = System.IO.Path.GetRelativePath(f.Root, dir);
        var rootName = System.IO.Path.GetFileName(f.Root);
        return rel == "." ? rootName : rootName + " › " + rel.Replace("\\", " › ");
    }
}
