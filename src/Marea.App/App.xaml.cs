using System.Drawing;
using System.Windows;
using Marea.App.Views;
using Marea.Core;
using Forms = System.Windows.Forms;

namespace Marea.App;

/// <summary>Application host: single instance, the engine (provider role), tray icon and the tide timer.</summary>
public partial class App : Application
{
    private const string ShowEventName = @"Local\Marea.App.Show.v1";
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _quitEvent;
    private Forms.NotifyIcon? _tray;
    private System.Threading.Timer? _tideTimer;
    private int _tideRunning;

    /// <summary>The engine shared by every view.</summary>
    public static MareaEngine Engine { get; private set; } = null!;

    /// <summary>The running application.</summary>
    public static new App Current => (App)Application.Current;

    /// <summary>True when the provider role could not be taken (another Marea serves the files).</summary>
    public bool ProviderElsewhere { get; private set; }

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out var created);
        if (!created)
        {
            _showEvent.Set(); // ask the running instance to show itself
            Shutdown();
            return;
        }
        Engine = new MareaEngine(Environment.GetEnvironmentVariable("MAREA_DATA"));
        Loc.SetLanguage(Engine.Settings.Language);
        ApplyTheme(Engine.Settings.Theme);
        ProviderElsewhere = !Engine.StartProvider();
        Engine.Hydration.Progress += OnHydration;
        CreateTray();
        new Thread(WaitForShowRequests) { IsBackground = true }.Start();
        // The uninstaller (marea restore-all) asks the app to step aside so it can bring files back.
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Marea.App.Quit.v1");
        new Thread(() => { if (_quitEvent.WaitOne()) Dispatcher.Invoke(Shutdown); }) { IsBackground = true }.Start();
        _tideTimer = new System.Threading.Timer(_ => _ = TideTickAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
        _ = CheckForUpdateAsync();
        if (!Engine.Settings.OnboardingDone)
        {
            new OnboardingWindow().Show();
        }
        else if (!e.Args.Contains("--tray"))
        {
            ShowMain();
        }
    }

    private void WaitForShowRequests()
    {
        while (_showEvent!.WaitOne())
        {
            Dispatcher.Invoke(ShowMain);
        }
    }

    /// <summary>Shows (or brings to front) the main window.</summary>
    public void ShowMain()
    {
        var w = Windows.OfType<MainWindow>().FirstOrDefault() ?? new MainWindow();
        w.Show();
        if (w.WindowState == WindowState.Minimized)
        {
            w.WindowState = WindowState.Normal;
        }
        w.Activate();
    }

    /// <summary>Switches between the system, light and dark Fluent themes at runtime.</summary>
    public static void ApplyTheme(string theme) =>
        Current.ThemeMode = theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

    /// <summary>Rebuilds the main window and tray menu after a language change.</summary>
    public void ReopenMainWindow()
    {
        foreach (var w in Windows.OfType<MainWindow>().ToList())
        {
            w.AllowClose = true;
            w.Close();
        }
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        CreateTray();
        var main = new MainWindow();
        main.Show();
        main.Navigate("settings");
    }

    private void CreateTray()
    {
        var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/marea.ico"))!.Stream;
        _tray = new Forms.NotifyIcon
        {
            Icon = new Icon(iconStream, new System.Drawing.Size(16, 16)),
            Text = "Marea",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add(Loc.T("tray.open"), null, (_, _) => ShowMain());
        _tray.ContextMenuStrip.Items.Add(Loc.T("tray.quick"), null, (_, _) => Dispatcher.Invoke(() => QuickFreeWindow.Open(null)));
        _tray.ContextMenuStrip.Items.Add(Loc.T("tray.exit"), null, (_, _) => Dispatcher.Invoke(ConfirmExit));
        _tray.DoubleClick += (_, _) => ShowMain();
        UpdateTrayText();
    }

    /// <summary>Refreshes the tray tooltip with the amount evaporated.</summary>
    public void UpdateTrayText()
    {
        if (_tray is null)
        {
            return;
        }
        var gone = Engine.Ledger.Files().Where(f => f.Residency == Residency.Evaporated).Sum(f => f.Size);
        var text = gone > 0 ? Loc.T("tray.some", Fmt.Gb(gone)) : Loc.T("tray.none");
        _tray.Text = text.Length > 63 ? text[..63] : text;
    }

    /// <summary>Shows a balloon notification from the tray.</summary>
    public void Notify(string title, string text, bool warning = false)
    {
        _tray?.ShowBalloonTip(5000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    private void OnHydration(HydrationEvent e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Finished && e.Error is not null)
            {
                Notify(Loc.T("tray.fail_title"), $"{System.IO.Path.GetFileName(e.Path)}: {e.Error}", true);
            }
            if (e.Finished)
            {
                UpdateTrayText();
            }
            Windows.OfType<MainWindow>().FirstOrDefault()?.ShowHydration(e);
        });
    }

    private async Task TideTickAsync()
    {
        if (!Engine.Settings.AutoTide || !Engine.IsProvider || Interlocked.Exchange(ref _tideRunning, 1) == 1)
        {
            return;
        }
        try
        {
            var freed = await Engine.RunTideAsync();
            if (freed > 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    Notify(Loc.T("tray.freed_title"), Loc.T("tray.freed_text", Fmt.Gb(freed)));
                    UpdateTrayText();
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CloudFiles.CloudFilesException or HttpRequestException)
        {
            Engine.Ledger.Log("j.tide_interrupted", "", 0, false, ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _tideRunning, 0);
        }
    }

    /// <summary>Once a day, asks GitHub for a newer release and only notifies (never installs).</summary>
    private async Task CheckForUpdateAsync()
    {
        var s = Engine.Settings;
        if (!s.CheckForUpdates || DateTime.UtcNow - s.LastUpdateCheckUtc < TimeSpan.FromHours(24))
        {
            return;
        }
        try
        {
            var update = await UpdateChecker.CheckAsync(UpdateChecker.Current);
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            s.AvailableUpdate = update?.Version.ToString(3);
            Engine.SaveSettings();
            if (update is not null)
            {
                await Dispatcher.InvokeAsync(() => Notify(Loc.T("upd.title", s.AvailableUpdate), Loc.T("upd.text")));
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Offline or GitHub unreachable: try again on the next start.
        }
    }

    /// <summary>Asks before quitting when evaporated files depend on this process.</summary>
    public void ConfirmExit()
    {
        var gone = Engine.Ledger.Files().Count(f => f.Residency == Residency.Evaporated);
        if (gone > 0 && Engine.IsProvider)
        {
            var r = MessageBox.Show(Loc.T("exit.confirm", gone), "Marea", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes)
            {
                return;
            }
        }
        Shutdown();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        _tideTimer?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        Engine?.Dispose();
        base.OnExit(e);
    }
}
