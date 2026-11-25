using System.Windows;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>Shows exactly what will evaporate, its source and the cost of coming back, and asks
/// for an explicit confirmation. Nothing is touched before the user presses the button.</summary>
public partial class PlanWindow : Window
{
    private readonly List<PlanItem> _plan;

    private PlanWindow(List<PlanItem> plan)
    {
        InitializeComponent();
        _plan = plan;
        Grid.ItemsSource = plan.Select(p => new
        {
            Name = System.IO.Path.GetFileName(p.File.Path),
            Folder = Fmt.RelativeFolder(p.File),
            Size = Fmt.Gb(p.File.Size),
            Class = Fmt.Class(p.File.Class),
            Return = Fmt.Duration(p.RebuildSeconds),
            Source = Loc.Render(p.File.SourceLabel),
        }).ToList();
        TotalText.Text = Loc.T("plan.total", Fmt.Gb(plan.Sum(p => p.File.Size)), plan.Count);
    }

    /// <summary>Opens the plan for everything allowed, or only for <paramref name="selection"/>.</summary>
    public static void ShowFor(MainWindow owner, IReadOnlyCollection<FileRecord>? selection)
    {
        var plan = App.Engine.Plan(0);
        if (selection is not null)
        {
            var paths = selection.Select(s => s.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            plan = plan.Where(p => paths.Contains(p.File.Path)).ToList();
        }
        if (plan.Count == 0)
        {
            MessageBox.Show(owner, Loc.T("plan.empty"), "Marea", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var w = new PlanWindow(plan) { Owner = owner };
        if (w.ShowDialog() == true)
        {
            _ = RunAsync(owner, plan.Select(p => p.File).ToList());
        }
    }

    private void OnUnderstand(object sender, RoutedEventArgs e) => GoButton.IsEnabled = Understand.IsChecked == true;

    private void OnGo(object sender, RoutedEventArgs e) => DialogResult = true;

    private static async Task RunAsync(MainWindow owner, List<FileRecord> files)
    {
        EnsureStartup(owner);
        List<(FileRecord File, string Why)> kept = [];
        var done = 0;
        await owner.RunAsync(Loc.T("op.evaporate"), async (p, ct) =>
        {
            (done, kept) = await App.Engine.EvaporateAsync(files, p, ct);
        });
        if (kept.Count > 0)
        {
            var lines = string.Join("\n", kept.Take(12).Select(k => $"• {System.IO.Path.GetFileName(k.File.Path)}: {k.Why}"));
            MessageBox.Show(owner, Loc.T("msg.kept_some", done, kept.Count, lines), "Marea", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Evaporated files need Marea running: offer to start it with Windows (once).</summary>
    private static void EnsureStartup(Window owner)
    {
        if (StartupRegistration.IsEnabled())
        {
            return;
        }
        if (MessageBox.Show(owner, Loc.T("startup.ask"), "Marea", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            StartupRegistration.Set(true);
            App.Engine.Settings.StartWithWindows = true;
            App.Engine.SaveSettings();
        }
    }
}
