using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>Overview: disk status, what is safely freeable, provenance breakdown, recent activity.</summary>
public partial class OverviewView : UserControl, IRefreshable
{
    /// <summary>Creates and loads the page.</summary>
    public OverviewView()
    {
        InitializeComponent();
        Refresh();
    }

    /// <inheritdoc/>
    public void Refresh()
    {
        var e = App.Engine;
        EmptyCard.Visibility = e.Settings.ManagedRoots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        VolumesList.ItemsSource = e.Volumes().Select(v => new
        {
            Title = Loc.T("ov.volume_title", v.Volume.TrimEnd('\\'), Fmt.Gb(v.FreeBytes)),
            Used = 1 - (double)v.FreeBytes / v.TotalBytes,
            Detail = Loc.T("ov.volume_detail", Fmt.Gb(v.TotalBytes - v.FreeBytes), Fmt.Gb(v.TotalBytes), Fmt.Gb(v.TargetBytes))
                     + Loc.T(e.Settings.AutoTide ? "ov.auto_on" : "ov.auto_off"),
            State = v.Deficit > 0 ? Loc.T("ov.below", Fmt.Gb(v.Deficit)) : Loc.T("ov.above"),
        }).ToList();

        var files = e.Ledger.Files();
        var allowed = e.Plan(0).Sum(p => p.File.Size);
        AllowedText.Text = Fmt.Gb(allowed);
        EvaporatedText.Text = Fmt.Gb(files.Where(f => f.Residency == Residency.Evaporated).Sum(f => f.Size));
        UniqueText.Text = Fmt.Gb(files.Where(f => f.Class == Recoverability.Unique).Sum(f => f.Size));
        TotalText.Text = Fmt.Gb(files.Sum(f => f.Size));
        FreeButton.IsEnabled = allowed > 0;
        QuickButton.IsEnabled = allowed > 0;
        var undo = e.LastUndoableBatch();
        UndoButton.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        if (undo is { } u)
        {
            UndoButton.Content = Loc.T("ov.undo_last", Fmt.Gb(u.Bytes), u.Files);
            UndoButton.Tag = u.Id;
        }

        var classes = new[] { Recoverability.Duplicate, Recoverability.Derivable, Recoverability.Reobtainable, Recoverability.Unique };
        var max = Math.Max(1, classes.Max(c => files.Where(f => f.Class == c).Sum(f => f.Size)));
        ClassBars.ItemsSource = classes.Select(c =>
        {
            var size = files.Where(f => f.Class == c).Sum(f => f.Size);
            return new
            {
                Label = Fmt.Class(c),
                Size = Fmt.Gb(size),
                BarWidth = Math.Max(size > 0 ? 6 : 0, 360.0 * size / max),
                Brush = (Brush)FindResource(c + "Brush"),
            };
        }).ToList();

        LastScan.Text = files.Count == 0 ? Loc.T("ov.no_scan")
            : Loc.T("ov.last_scan", files.Count, Fmt.Ago(files.Max(f => f.HashedUtc)));
        Recent.ItemsSource = e.Ledger.Journal(6).Select(j => new
        {
            Action = (j.Ok ? "" : "⚠ ") + Loc.Render(j.Action),
            What = string.IsNullOrEmpty(j.Path) ? Loc.Render(j.Detail)
                 : System.IO.Path.GetFileName(j.Path) + (j.Bytes > 0 ? $" · {Fmt.Gb(j.Bytes)}" : ""),
            When = Fmt.Ago(j.AtUtc),
        }).ToList();
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        FoldersView.AddFolderInteractive(Window.GetWindow(this));
        Refresh();
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await ScanAsync((MainWindow)Window.GetWindow(this));

    /// <summary>Scan + source search with progress in the shell.</summary>
    public static Task ScanAsync(MainWindow w) => w.RunAsync(Loc.T("op.scan"), async (p, ct) =>
    {
        await App.Engine.ScanAsync(p, ct);
        await App.Engine.RefreshSourcesAsync(p, ct);
    });

    private void OnQuick(object sender, RoutedEventArgs e) => QuickFreeWindow.Open(Window.GetWindow(this));

    private async void OnUndo(object sender, RoutedEventArgs e)
    {
        var batch = (string)UndoButton.Tag;
        string? problem = null;
        await ((MainWindow)Window.GetWindow(this)).RunAsync(Loc.T("op.undo"), async (p, ct) =>
            problem = await App.Engine.UndoBatchAsync(batch, p, ct));
        if (problem is not null)
        {
            MessageBox.Show(Window.GetWindow(this), problem, "Marea", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnFree(object sender, RoutedEventArgs e) => PlanWindow.ShowFor((MainWindow)Window.GetWindow(this), null);
}
