using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>Overview: what can be freed safely right now, drive status, provenance breakdown, recent activity.</summary>
public partial class OverviewView : UserControl, IRefreshable
{
    private static readonly Recoverability[] Classes =
        [Recoverability.Duplicate, Recoverability.Derivable, Recoverability.Reobtainable, Recoverability.Unique];

    /// <summary>Creates and loads the page.</summary>
    public OverviewView()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>Warns, without alarm, when some models could run code if loaded.</summary>
    private void ShowSafety(List<ModelEntry> models)
    {
        var risky = models.Where(m => m.Safety >= SafetyVerdict.Suspicious).ToList();
        SafetyBar.Visibility = risky.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var dangerous = risky.Any(m => m.Safety == SafetyVerdict.Dangerous);
        var key = dangerous ? "ov.safety_dangerous" : "ov.safety_title";
        SafetyTitle.Text = Loc.T(risky.Count == 1 ? key + "_one" : key, risky.Count);
        SafetyIcon.Fill = (Brush)FindResource(dangerous ? "DangerBrush" : "WarningBrush");
        SafetyBar.BorderBrush = SafetyIcon.Fill;
    }

    /// <summary>Shows how many files depend only on public sources that are disappearing or gone.</summary>
    private void ShowRisks(List<WatchFinding> risks)
    {
        RiskBar.Visibility = risks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var lost = risks.Count(r => r.Action == WatchAction.Lost);
        var key = lost > 0 ? "ov.risk_lost" : "ov.risk_title";
        var n = lost > 0 ? lost : risks.Count;
        RiskTitle.Text = Loc.T(n == 1 ? key + "_one" : key, n);
        RiskIcon.Fill = (Brush)FindResource(lost > 0 ? "DangerBrush" : "WarningBrush");
        RiskIcon.Data = (Geometry)FindResource(lost > 0 ? "Icon.ShieldError" : "Icon.Warning");
        RiskBar.BorderBrush = RiskIcon.Fill;
    }

    private void OnReviewModels(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow(this)).Navigate("models");

    /// <inheritdoc/>
    public void Refresh()
    {
        var e = App.Engine;
        var hasRoots = e.Settings.ManagedRoots.Count > 0;
        EmptyCard.Visibility = hasRoots ? Visibility.Collapsed : Visibility.Visible;
        foreach (var part in new FrameworkElement[] { HeroCard, StatsRow, BreakdownCard, RecentCard })
        {
            part.Visibility = hasRoots ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateBar.Visibility = e.Settings.AvailableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateText.Text = Loc.T("upd.title", e.Settings.AvailableUpdate);
        ShowSafety(e.Models());
        ShowRisks(e.SourceRisks());

        VolumesList.ItemsSource = e.Volumes().Select(v => new
        {
            Title = Loc.T("ov.volume_title", v.Volume.TrimEnd('\\'), Fmt.Gb(v.FreeBytes)),
            Used = 1 - (double)v.FreeBytes / v.TotalBytes,
            Detail = Loc.T("ov.volume_detail", Fmt.Gb(v.TotalBytes - v.FreeBytes), Fmt.Gb(v.TotalBytes), Fmt.Gb(v.TargetBytes))
                     + Loc.T(e.Settings.AutoTide ? "ov.auto_on" : "ov.auto_off"),
            State = v.Deficit > 0 ? Loc.T("ov.below", Fmt.Gb(v.Deficit)) : Loc.T("ov.above"),
            StateBrush = (Brush)FindResource(v.Deficit > 0 ? "WarningBrush" : "DuplicateBrush"),
        }).ToList();

        var files = e.Ledger.Files();
        var plan = e.Plan(0);
        var instant = plan.Where(p => p.Instant).Sum(p => p.File.Size);
        var allowed = plan.Sum(p => p.File.Size);
        AllowedText.Text = Fmt.Gb(allowed);
        AllowedDetail.Text = allowed > 0 ? Loc.T("ov.allowed_detail", plan.Count, Fmt.Gb(instant)) : Loc.T("ov.allowed_none");
        QuickButton.IsEnabled = FreeButton.IsEnabled = allowed > 0;
        EvaporatedText.Text = Fmt.Gb(files.Where(f => f.Residency == Residency.Evaporated).Sum(f => f.Size));
        UniqueText.Text = Fmt.Gb(files.Where(f => f.Class == Recoverability.Unique).Sum(f => f.Size));
        TotalText.Text = Fmt.Gb(files.Sum(f => f.Size));
        LastScan.Text = files.Count == 0 ? Loc.T("ov.no_scan") : Loc.T("ov.last_scan", files.Count, Fmt.Ago(files.Max(f => f.HashedUtc)));

        BuildBreakdown(files);
        BuildRecent();

        var undo = e.LastUndoableBatch();
        UndoBar.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        if (undo is { } u)
        {
            UndoText.Text = Loc.T("ov.undo_last", Fmt.Gb(u.Bytes), u.Files);
            UndoButton.Tag = u.Id;
        }
    }

    /// <summary>Stacked bar (one coloured segment per class) and legend with icons.</summary>
    private void BuildBreakdown(List<FileRecord> files)
    {
        StackedBar.ColumnDefinitions.Clear();
        StackedBar.Children.Clear();
        var col = 0;
        foreach (var c in Classes)
        {
            var size = files.Where(f => f.Class == c).Sum(f => f.Size);
            if (size == 0)
            {
                continue;
            }
            StackedBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(size, GridUnitType.Star) });
            var seg = new Border { Background = (Brush)FindResource(c + "Brush"), ToolTip = $"{Fmt.Class(c)} · {Fmt.Gb(size)}" };
            Grid.SetColumn(seg, col++);
            StackedBar.Children.Add(seg);
        }
        Legend.ItemsSource = Classes.Select(c => new
        {
            Label = Fmt.Class(c),
            Size = Fmt.Gb(files.Where(f => f.Class == c).Sum(f => f.Size)),
            Help = Loc.T("class_help." + c.ToString().ToLowerInvariant()),
            Brush = (Brush)FindResource(c + "Brush"),
            Icon = (Geometry)FindResource(Fmt.ClassIcon(c)),
        }).ToList();
    }

    /// <summary>Rounds the stacked bar exactly, whatever the window width.</summary>
    private void OnBarSizeChanged(object sender, SizeChangedEventArgs e) =>
        StackedBar.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);

    /// <summary>Last activity lines, each with an icon matching what happened.</summary>
    private void BuildRecent()
    {
        Recent.ItemsSource = App.Engine.Ledger.Journal(6).Select(j =>
        {
            var (icon, brush) = Fmt.JournalIcon(j);
            return new
            {
                Icon = (Geometry)FindResource(icon),
                Brush = (Brush)FindResource(brush),
                Action = Loc.Render(j.Action),
                What = string.IsNullOrEmpty(j.Path) ? Fmt.Source(j.Detail)
                     : System.IO.Path.GetFileName(j.Path) + (j.Bytes > 0 ? $" · {Fmt.Gb(j.Bytes)}" : ""),
                When = Fmt.Ago(j.AtUtc),
            };
        }).ToList();
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        if (FoldersView.AddFolderInteractive(Window.GetWindow(this)))
        {
            _ = ScanAsync((MainWindow)Window.GetWindow(this));
        }
        Refresh();
    }

    private void OnFindFolders(object sender, RoutedEventArgs e)
    {
        new OnboardingWindow(findOnly: true) { Owner = Window.GetWindow(this) }.ShowDialog();
        if (App.Engine.Settings.ManagedRoots.Count > 0)
        {
            _ = ScanAsync((MainWindow)Window.GetWindow(this));
        }
        Refresh();
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateChecker.ReleasesPage.ToString()) { UseShellExecute = true });

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
