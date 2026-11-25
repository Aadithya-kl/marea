using System.Windows;
using System.Windows.Controls;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>Every tracked file: class, source, state; per-file explanation of why it stays or how it returns.</summary>
public partial class FilesView : UserControl, IRefreshable
{
    private sealed record Row(FileRecord File, string Name, string Folder, string Size, long Bytes, string Class, string State, string LastUse);

    private Dictionary<string, GateVerdict> _verdicts = [];

    /// <summary>Creates and loads the page.</summary>
    public FilesView()
    {
        InitializeComponent();
        Filter.ItemsSource = new[] { Loc.T("files.filter_all") }
            .Concat(new[] { Recoverability.Duplicate, Recoverability.Derivable, Recoverability.Reobtainable, Recoverability.Unique }.Select(Fmt.Class))
            .Append(Fmt.Residency(Residency.Evaporated)).ToList();
        Filter.SelectedIndex = 0;
    }

    /// <inheritdoc/>
    public void Refresh()
    {
        _verdicts = App.Engine.Evaluate().ToDictionary(v => v.File.Path, StringComparer.OrdinalIgnoreCase);
        var files = App.Engine.Ledger.Files();
        var f = Filter.SelectedIndex;
        if (f is >= 1 and <= 4)
        {
            var cls = new[] { Recoverability.Duplicate, Recoverability.Derivable, Recoverability.Reobtainable, Recoverability.Unique }[f - 1];
            files = files.Where(x => x.Class == cls).ToList();
        }
        else if (f == 5)
        {
            files = files.Where(x => x.Residency == Residency.Evaporated).ToList();
        }
        Grid.ItemsSource = files.Select(x => new Row(x, System.IO.Path.GetFileName(x.Path), Fmt.RelativeFolder(x),
            Fmt.Gb(x.Size), x.Size, Fmt.Class(x.Class), Fmt.Residency(x.Residency), Fmt.Ago(x.LastAccessUtc))).ToList();
        ReplicateButton.IsEnabled = !string.IsNullOrEmpty(App.Engine.Settings.MirrorRoot);
        OnSelection(this, null!);
    }

    private void OnFilter(object sender, SelectionChangedEventArgs e) => Refresh();

    private List<FileRecord> Selected() => Grid.SelectedItems.Cast<Row>().Select(r => r.File).ToList();

    private void OnSelection(object sender, SelectionChangedEventArgs e)
    {
        var sel = Selected();
        EvaporateButton.IsEnabled = sel.Any(s => _verdicts.TryGetValue(s.Path, out var v) && v.Allowed);
        BackButton.IsEnabled = sel.Any(s => s.Residency == Residency.Evaporated);
        if (sel.Count != 1)
        {
            DetailTitle.Text = sel.Count == 0 ? "" : Loc.T("files.selected", sel.Count, Fmt.Gb(sel.Sum(s => s.Size)));
            DetailText.Text = sel.Count == 0 ? Loc.T("files.select_hint") : "";
            return;
        }
        var file = sel[0];
        DetailTitle.Text = System.IO.Path.GetFileName(file.Path);
        var source = Loc.T("files.source_line", Fmt.Source(file.SourceLabel));
        if (file.Residency == Residency.Evaporated)
        {
            DetailText.Text = Loc.T("files.is_evaporated", Fmt.Ago(file.EvaporatedUtc ?? default)) + "\n" + source;
        }
        else if (_verdicts.TryGetValue(file.Path, out var v))
        {
            DetailText.Text = v.Allowed
                ? Loc.T("files.can_go", Fmt.Duration(v.RebuildSeconds)) + "\n" + source
                : Loc.T("files.why_stays") + "\n• " + string.Join("\n• ", v.Blockers.Select(b => b.Text)) + "\n" + source;
        }
    }

    private void OnEvaporate(object sender, RoutedEventArgs e) =>
        PlanWindow.ShowFor((MainWindow)Window.GetWindow(this), Selected());

    private async void OnBringBack(object sender, RoutedEventArgs e)
    {
        var files = Selected().Where(s => s.Residency == Residency.Evaporated).ToList();
        string? problem = null;
        await ((MainWindow)Window.GetWindow(this)).RunAsync(Loc.T("op.bring_back"), async (p, ct) =>
            problem = await App.Engine.BringBackAsync(files, p, ct));
        if (problem is not null)
        {
            MessageBox.Show(Window.GetWindow(this), problem, "Marea", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnReplicate(object sender, RoutedEventArgs e)
    {
        var files = Selected().Where(s => s.Residency == Residency.Local).ToList();
        await ((MainWindow)Window.GetWindow(this)).RunAsync(Loc.T("op.replicate"), async (p, ct) =>
            await App.Engine.ReplicateAsync(files, p, ct));
    }
}
