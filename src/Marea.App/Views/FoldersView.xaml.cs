using System.Windows;
using System.Windows.Controls;
using Marea.Core;
using Microsoft.Win32;

namespace Marea.App.Views;

/// <summary>Managed folders: add (validated), discover automatically, stop managing (brings everything back first).</summary>
public partial class FoldersView : UserControl, IRefreshable
{
    /// <summary>Creates and loads the page.</summary>
    public FoldersView()
    {
        InitializeComponent();
        Refresh();
    }

    /// <inheritdoc/>
    public void Refresh()
    {
        var files = App.Engine.Ledger.Files();
        Roots.ItemsSource = App.Engine.Settings.ManagedRoots.Select(r =>
        {
            var inRoot = files.Where(f => string.Equals(f.Root, r, StringComparison.OrdinalIgnoreCase)).ToList();
            var gone = inRoot.Where(f => f.Residency == Residency.Evaporated).Sum(f => f.Size);
            return new
            {
                Path = r,
                Display = Fmt.HomePath(r),
                Name = System.IO.Path.GetFileName(r.TrimEnd(System.IO.Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : r,
                Summary = Loc.T("folders.summary", inRoot.Count, Fmt.Gb(inRoot.Sum(f => f.Size)), Fmt.Gb(gone)),
            };
        }).ToList();
    }

    /// <summary>Folder picker + validation; returns true when a folder was added.</summary>
    public static bool AddFolderInteractive(Window owner)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("folders.pick") };
        if (dlg.ShowDialog(owner) != true)
        {
            return false;
        }
        var why = App.Engine.AddRoot(dlg.FolderName);
        if (why is not null)
        {
            MessageBox.Show(owner, Loc.T("folders.refused", why), "Marea", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        if (AddFolderInteractive(Window.GetWindow(this)))
        {
            Refresh();
            await OverviewView.ScanAsync((MainWindow)Window.GetWindow(this));
        }
    }

    private void OnFind(object sender, RoutedEventArgs e)
    {
        new OnboardingWindow(findOnly: true) { Owner = Window.GetWindow(this) }.ShowDialog();
        Refresh();
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        var root = (string)((Button)sender).Tag;
        var owner = Window.GetWindow(this);
        if (MessageBox.Show(owner, Loc.T("folders.confirm_remove", root), "Marea", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        string? problem = null;
        await ((MainWindow)owner).RunAsync(Loc.T("op.remove_root"), async (p, ct) => problem = await App.Engine.RemoveRootAsync(root, p, ct));
        if (problem is not null)
        {
            MessageBox.Show(owner, problem, "Marea", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Refresh();
    }
}
