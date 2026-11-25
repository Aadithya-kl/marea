using System.Windows;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>"Free up space in 3 clicks": how much, how it must come back, go. Uses exactly the same
/// plan, safety gate and evaporation as the detailed view; ends with a one-click undo of this batch.</summary>
public partial class QuickFreeWindow : Window
{
    private const long Gb = 1L << 30;
    private readonly long _instant;
    private readonly long _all;
    private readonly long _deficit;
    private List<PlanItem> _plan = [];
    private CancellationTokenSource? _cts;
    private string? _batch;

    /// <summary>Opens the wizard (or a message when nothing can be freed safely).</summary>
    public static void Open(Window? owner)
    {
        var (instant, all) = App.Engine.QuickAvailability();
        if (all == 0)
        {
            MessageBox.Show(owner!, Loc.T("qf.nothing"), "Marea", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var w = new QuickFreeWindow(instant, all);
        if (owner is { IsVisible: true })
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        w.Show();
        w.Activate();
    }

    private QuickFreeWindow(long instant, long all)
    {
        InitializeComponent();
        _instant = instant;
        _all = all;
        _deficit = App.Engine.Volumes().Sum(v => v.Deficit);
        ModeInstantTitle.Text = Loc.T("qf.instant", Fmt.Gb(instant));
        ModeAllTitle.Text = Loc.T("qf.download", Fmt.Gb(all));
        AmountTarget.IsEnabled = _deficit > 0;
        AmountTargetNote.Text = _deficit > 0 ? Fmt.Gb(_deficit) : Loc.T("qf.above_target");
        AmountAllNote.Text = Loc.T("qf.all_note");
        (instant > 0 ? ModeInstant : ModeAll).IsChecked = true;
        ModeInstant.IsEnabled = instant > 0;
        (_deficit > 0 ? AmountTarget : AmountAll).IsChecked = true;
        Recompute();
    }

    private bool InstantOnly => ModeInstant.IsChecked == true;

    private long Available => InstantOnly ? _instant : _all;

    private long Requested =>
        Amount10.IsChecked == true ? 10 * Gb :
        Amount50.IsChecked == true ? 50 * Gb :
        AmountTarget.IsChecked == true ? _deficit : 0;

    private void OnChoice(object sender, RoutedEventArgs e)
    {
        if (IsLoaded || Summary is not null)
        {
            Recompute();
        }
    }

    /// <summary>Rebuilds the plan for the current choice and the one-sentence summary.</summary>
    private void Recompute()
    {
        if (Summary is null || ModeAllTitle is null)
        {
            return;
        }
        foreach (var (note, gb) in new[] { (Amount10Note, 10 * Gb), (Amount50Note, 50 * Gb) })
        {
            note.Text = Available >= gb ? Loc.T("qf.available") : Loc.T("qf.only", Fmt.Gb(Available));
        }
        _plan = App.Engine.Plan(Requested, InstantOnly);
        var bytes = _plan.Sum(p => p.File.Size);
        GoButton.IsEnabled = _plan.Count > 0;
        if (_plan.Count == 0)
        {
            Summary.Text = Loc.T("qf.nothing");
            return;
        }
        var slowest = _plan.Max(p => p.RebuildSeconds);
        Summary.Text = Loc.T("qf.summary", Fmt.Gb(bytes), _plan.Count, Fmt.Duration(slowest))
                       + (Requested > 0 && bytes < Requested ? " " + Loc.T("qf.less_than_asked", Fmt.Gb(Requested)) : "");
    }

    private async void OnGo(object sender, RoutedEventArgs e)
    {
        PlanWindow.EnsureStartup(this);
        ChoosePanel.Visibility = Visibility.Collapsed;
        WorkPanel.Visibility = Visibility.Visible;
        _cts = new CancellationTokenSource();
        _batch = "quick-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var files = _plan.Select(p => p.File).ToList();
        var progress = new Progress<ScanProgress>(p =>
        {
            WorkDetail.Text = $"{p.Phase}: {System.IO.Path.GetFileName(p.Path)}";
            WorkProgress.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
        });
        var done = 0;
        List<(FileRecord File, string Why)> kept = [];
        try
        {
            (done, kept) = await Task.Run(() => App.Engine.EvaporateAsync(files, progress, _cts.Token, _batch));
        }
        catch (OperationCanceledException)
        {
            // Files already evaporated stay in the batch and can be undone below.
        }
        ShowDone(files, kept);
    }

    private void ShowDone(List<FileRecord> files, List<(FileRecord File, string Why)> kept)
    {
        var freed = App.Engine.Ledger.Files().Where(f => f.Batch == _batch && f.Residency == Residency.Evaporated).ToList();
        WorkPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Visible;
        DoneTitle.Text = Loc.T("qf.done_title", Fmt.Gb(freed.Sum(f => f.Size)));
        DoneText.Text = Loc.T("qf.done_text", freed.Count);
        DoneKept.Text = kept.Count == 0 ? "" : Loc.T("qf.done_kept", kept.Count)
            + "\n" + string.Join("\n", kept.Take(5).Select(k => $"• {System.IO.Path.GetFileName(k.File.Path)}: {k.Why}"));
        UndoButton.IsEnabled = freed.Count > 0;
        App.Current.UpdateTrayText();
        foreach (var w in Application.Current.Windows.OfType<MainWindow>())
        {
            w.RefreshPage();
        }
    }

    private void OnStop(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async void OnUndo(object sender, RoutedEventArgs e)
    {
        UndoButton.IsEnabled = false;
        DoneTitle.Text = Loc.T("qf.undoing");
        var problem = await Task.Run(() => App.Engine.UndoBatchAsync(_batch!));
        DoneTitle.Text = problem is null ? Loc.T("qf.undone") : problem;
        DoneText.Text = "";
        DoneKept.Text = "";
        App.Current.UpdateTrayText();
        foreach (var w in Application.Current.Windows.OfType<MainWindow>())
        {
            w.RefreshPage();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
