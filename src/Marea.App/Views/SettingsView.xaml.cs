using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Marea.Core;
using Microsoft.Win32;

namespace Marea.App.Views;

/// <summary>Settings with validation, language switch, start-with-Windows and the emergency exit.</summary>
public partial class SettingsView : UserControl, IRefreshable
{
    private sealed record LangItem(string Code, string Name);

    /// <summary>Creates and loads the page.</summary>
    public SettingsView()
    {
        InitializeComponent();
        Refresh();
    }

    /// <inheritdoc/>
    public void Refresh()
    {
        var s = App.Engine.Settings;
        var langs = new[] { new LangItem("auto", Loc.T("set.lang_auto")) }
            .Concat(Loc.Languages.Select(l => new LangItem(l.Code, l.Name))).ToList();
        LanguageBox.ItemsSource = langs;
        LanguageBox.SelectedItem = langs.FirstOrDefault(l => l.Code == s.Language) ?? langs[0];
        var themes = new[] { "system", "light", "dark" }.Select(t => new LangItem(t, Loc.T("set.theme_" + t))).ToList();
        _loading = true;
        ThemeBox.ItemsSource = themes;
        ThemeBox.SelectedItem = themes.FirstOrDefault(t => t.Code == s.Theme) ?? themes[0];
        _loading = false;
        AutoTide.IsChecked = s.AutoTide;
        Target.Text = s.FreeSpaceTargetGb.ToString(CultureInfo.CurrentCulture);
        Idle.Text = s.MinIdleDays.ToString(CultureInfo.CurrentCulture);
        MinSize.Text = s.MinFileSizeMb.ToString(CultureInfo.CurrentCulture);
        Bandwidth.Text = s.BandwidthMBps.ToString(CultureInfo.CurrentCulture);
        Mirror.Text = s.MirrorRoot ?? "";
        Repos.Text = string.Join(Environment.NewLine, s.HuggingFaceRepos);
        Startup.IsChecked = StartupRegistration.IsEnabled();
        Discover.IsChecked = s.DiscoverHubRepos;
        NameSearch.IsChecked = s.HubNameSearch;
        Ollama.IsChecked = s.OllamaSource;
        Watch.IsChecked = s.WatchSources;
        Rescue.IsChecked = s.RescueAtRisk;
        Updates.IsChecked = s.CheckForUpdates;
        UpdateCard.Visibility = s.AvailableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateTitle.Text = Loc.T("upd.title", s.AvailableUpdate);
        AboutText.Text = Loc.T("set.about", typeof(App).Assembly.GetName().Version?.ToString(3), App.Engine.DataDir);
    }

    private void OnPickMirror(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("set.mirror") };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            Mirror.Text = dlg.FolderName;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var s = App.Engine.Settings;
        if (!double.TryParse(Target.Text, CultureInfo.CurrentCulture, out var target) || target < 0
            || !int.TryParse(Idle.Text, out var idle) || idle < 0
            || !long.TryParse(MinSize.Text, out var min) || min < 1
            || !double.TryParse(Bandwidth.Text, CultureInfo.CurrentCulture, out var bw) || bw <= 0)
        {
            SavedText.Text = Loc.T("set.invalid");
            return;
        }
        var repos = Repos.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        var mirror = Mirror.Text.Trim();
        if (mirror.Length > 0 && !System.IO.Directory.Exists(mirror))
        {
            SavedText.Text = Loc.T("set.mirror_missing");
            return;
        }
        var newLang = ((LangItem)LanguageBox.SelectedItem).Code;
        var langChanged = newLang != s.Language;
        s.FreeSpaceTargetGb = target;
        s.MinIdleDays = idle;
        s.MinFileSizeMb = min;
        s.BandwidthMBps = bw;
        s.AutoTide = AutoTide.IsChecked == true;
        s.MirrorRoot = mirror.Length > 0 ? mirror : null;
        s.HuggingFaceRepos = repos;
        s.Language = newLang;
        s.StartWithWindows = Startup.IsChecked == true;
        s.DiscoverHubRepos = Discover.IsChecked == true;
        s.HubNameSearch = NameSearch.IsChecked == true;
        s.OllamaSource = Ollama.IsChecked == true;
        s.WatchSources = Watch.IsChecked == true;
        s.RescueAtRisk = Rescue.IsChecked == true;
        s.CheckForUpdates = Updates.IsChecked == true;
        s.Theme = ((LangItem)ThemeBox.SelectedItem).Code;
        StartupRegistration.Set(s.StartWithWindows);
        App.Engine.SaveSettings();
        if (langChanged)
        {
            Loc.SetLanguage(newLang);
            App.Current.ReopenMainWindow();
            return;
        }
        SavedText.Text = Loc.T("set.saved");
    }

    private bool _loading;

    /// <summary>Applies the chosen theme immediately (saved with the other settings).</summary>
    private void OnThemePreview(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeBox.SelectedItem is LangItem t)
        {
            App.ApplyTheme(t.Code);
        }
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateChecker.ReleasesPage.ToString()) { UseShellExecute = true });

    private async void OnRestoreAll(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (MessageBox.Show(owner, Loc.T("set.restore_confirm"), "Marea", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        string? problem = null;
        await ((MainWindow)owner).RunAsync(Loc.T("op.restore_all"), async (p, ct) => problem = await App.Engine.RestoreEverythingAsync(p, ct));
        MessageBox.Show(owner, problem ?? Loc.T("set.restore_done"), "Marea", MessageBoxButton.OK,
            problem is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
