using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Marea.Core;
using Microsoft.Win32;

namespace Marea.App.Views;

/// <summary>First-run guide: what Marea does and never does, then the model folders it found
/// (pre-selected, editable). Nothing is evaporated here: the first scan only measures.</summary>
public partial class OnboardingWindow : Window
{
    /// <summary>A proposed folder.</summary>
    public sealed class Candidate : INotifyPropertyChanged
    {
        private bool _selected = true;

        /// <summary>Folder path.</summary>
        public required string Path { get; init; }

        /// <summary>Path as shown, with the user profile shortened to "~".</summary>
        public string Display => Fmt.HomePath(Path);
        /// <summary>Size and count summary.</summary>
        public required string Summary { get; init; }

        /// <summary>Whether it will be managed.</summary>
        public bool Selected
        {
            get => _selected;
            set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); }
        }

        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<Candidate> _candidates = [];
    private readonly bool _findOnly;

    /// <summary>Creates the guide; <paramref name="findOnly"/> shows only the folder step.</summary>
    public OnboardingWindow(bool findOnly = false)
    {
        InitializeComponent();
        _findOnly = findOnly;
        Found.ItemsSource = _candidates;
        StepText.Text = Loc.T("ob.step", 1, 2);
        if (findOnly)
        {
            ShowFolders();
        }
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (Step1.Visibility == Visibility.Visible)
        {
            ShowFolders();
            return;
        }
        Finish();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        Step2.Visibility = Visibility.Collapsed;
        Step1.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        NextButton.Content = Loc.T("ob.next");
        StepText.Text = Loc.T("ob.step", 1, 2);
    }

    private async void ShowFolders()
    {
        Step1.Visibility = Visibility.Collapsed;
        Step2.Visibility = Visibility.Visible;
        BackButton.Visibility = _findOnly ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = Loc.T(_findOnly ? "ob.add_selected" : "ob.finish");
        StepText.Text = _findOnly ? "" : Loc.T("ob.step", 2, 2);
        if (_candidates.Count > 0)
        {
            return;
        }
        var managed = App.Engine.Settings.ManagedRoots;
        var roots = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        roots.AddRange(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady
            && !string.Equals(d.Name, System.IO.Path.GetPathRoot(roots[0]), StringComparison.OrdinalIgnoreCase)).Select(d => d.Name));
        var found = await Task.Run(() => ModelFolderFinder.Find(roots));
        foreach (var f in found.Where(f => FileSystemFacts.RejectRoot(f.Path, managed) is null))
        {
            _candidates.Add(new Candidate { Path = f.Path, Summary = Loc.T("ob.found_summary", f.ModelFiles, Fmt.Gb(f.ModelBytes)) });
        }
        Searching.Visibility = Visibility.Collapsed;
        FoldersSubtitle.Text = _candidates.Count > 0 ? Loc.T("ob.found", _candidates.Count) : Loc.T("ob.none_found");
    }

    private void OnAddOther(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("folders.pick") };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }
        var why = FileSystemFacts.RejectRoot(dlg.FolderName, App.Engine.Settings.ManagedRoots.Concat(_candidates.Where(c => c.Selected).Select(c => c.Path)));
        if (why is not null)
        {
            MessageBox.Show(this, Loc.T("folders.refused", why), "Marea", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _candidates.Insert(0, new Candidate { Path = dlg.FolderName, Summary = Loc.T("ob.added_by_you") });
    }

    private void Finish()
    {
        var refused = new List<string>();
        foreach (var c in _candidates.Where(c => c.Selected))
        {
            if (App.Engine.AddRoot(c.Path) is { } why)
            {
                refused.Add($"{c.Path}: {why}");
            }
        }
        if (refused.Count > 0)
        {
            MessageBox.Show(this, Loc.T("folders.refused", string.Join("\n", refused)), "Marea", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (_findOnly)
        {
            DialogResult = true;
            return;
        }
        App.Engine.Settings.OnboardingDone = true;
        App.Engine.SaveSettings();
        App.Current.ShowMain();
        var main = Application.Current.Windows.OfType<MainWindow>().First();
        Close();
        if (App.Engine.Settings.ManagedRoots.Count > 0)
        {
            _ = OverviewView.ScanAsync(main);
        }
    }
}
