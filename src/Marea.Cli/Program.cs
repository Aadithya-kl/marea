using Marea.Core;

// Marea CLI: same engine as the desktop app, for scripts and power users.
// Destructive actions print the plan and require --yes. MAREA_DATA overrides the data folder.

var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
using var engine = new MareaEngine(Environment.GetEnvironmentVariable("MAREA_DATA"));
Loc.SetLanguage(Environment.GetEnvironmentVariable("MAREA_LANG") ?? engine.Settings.Language);
var progress = new Progress<ScanProgress>(p =>
{
    var pct = p.Total > 0 ? $" {100.0 * p.Done / p.Total,5:0.0}%" : "";
    Console.Error.Write($"\r{p.Phase}{pct}  {Short(p.Path),-70}");
});

switch (cmd)
{
    case "status":
        Status();
        break;
    case "add":
        Console.WriteLine(engine.AddRoot(Arg(1)) ?? Loc.T("cli.added"));
        break;
    case "remove":
        engine.StartProvider();
        Console.WriteLine(await engine.RemoveRootAsync(Arg(1), progress) ?? Loc.T("cli.removed"));
        break;
    case "scan":
        var hashed = await engine.ScanAsync(progress);
        await engine.RefreshSourcesAsync(progress);
        Console.Error.WriteLine();
        Console.WriteLine(Loc.T("cli.scanned", hashed));
        Status();
        break;
    case "plan":
        PrintPlan(engine.Plan(GbArg()));
        break;
    case "evaporate":
        {
            var plan = engine.Plan(GbArg());
            PrintPlan(plan);
            if (!args.Contains("--yes"))
            {
                Console.WriteLine(Loc.T("cli.need_yes"));
                break;
            }
            engine.StartProvider();
            var (done, kept) = await engine.EvaporateAsync(plan.Select(p => p.File), progress);
            Console.Error.WriteLine();
            Console.WriteLine(Loc.T("cli.evaporated", done));
            foreach (var (f, why) in kept)
            {
                Console.WriteLine($"  {Loc.T("cli.kept")}: {f.Path} -> {why}");
            }
            break;
        }
    case "restore":
        engine.StartProvider();
        var rec = engine.Ledger.Get(Path.GetFullPath(Arg(1)));
        Console.WriteLine(rec is null ? Loc.T("cli.not_tracked") : await engine.BringBackAsync([rec], progress) ?? Loc.T("cli.restored"));
        break;
    case "restore-all":
        if (!args.Contains("--yes"))
        {
            Console.WriteLine(Loc.T("cli.restore_all_need_yes"));
            break;
        }
        if (!await TakeProviderRoleAsync())
        {
            Console.WriteLine(Loc.T("cli.close_app_first"));
            return 2;
        }
        var problem = await engine.RestoreEverythingAsync(progress);
        Console.Error.WriteLine();
        Console.WriteLine(problem ?? Loc.T("cli.restored_all"));
        if (problem is null && args.Contains("--uninstall"))
        {
            RemoveStartupEntry();
        }
        return problem is null ? 0 : 1;
    case "replicate":
        engine.StartProvider();
        var unique = engine.Ledger.Files().Where(f => f.Class == Recoverability.Unique && f.Residency == Residency.Local).ToList();
        Console.WriteLine(Loc.T("cli.replicated", await engine.ReplicateAsync(unique, progress)));
        break;
    case "journal":
        foreach (var j in engine.Ledger.Journal(int.TryParse(args.ElementAtOrDefault(1), out var n) ? n : 30))
        {
            Console.WriteLine($"{j.AtUtc.ToLocalTime():g}  {(j.Ok ? " " : "!")} {Loc.Render(j.Action),-26} {Gb(j.Bytes),9}  {Short(j.Path)}  {Loc.Render(j.Detail)}");
        }
        break;
    case "agent":
        if (!engine.StartProvider())
        {
            Console.WriteLine(Loc.T("cli.already_serving"));
            return 1;
        }
        Console.WriteLine(Loc.T("cli.agent_listening"));
        engine.Hydration.Progress += e =>
        {
            if (e.Finished)
            {
                Console.WriteLine($"{(e.Error is null ? Loc.T("cli.returned") : Loc.T("cli.error"))}: {e.Path} {e.Error}");
            }
        };
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        while (!stop.Task.IsCompleted)
        {
            if (engine.Settings.AutoTide)
            {
                var freed = await engine.RunTideAsync(progress);
                if (freed > 0)
                {
                    Console.WriteLine("\n" + Loc.T("cli.tide_freed", Gb(freed)));
                }
            }
            await Task.WhenAny(stop.Task, Task.Delay(TimeSpan.FromMinutes(10)));
        }
        break;
    default:
        Console.WriteLine(Loc.T("cli.help"));
        break;
}
return 0;

// The desktop app holds the provider role; Windows cannot detach a folder while it is connected.
// Ask it to quit (same user session) and wait for the role to free up.
async Task<bool> TakeProviderRoleAsync()
{
    if (engine.StartProvider())
    {
        return true;
    }
    if (EventWaitHandle.TryOpenExisting(@"Local\Marea.App.Quit.v1", out var quit))
    {
        quit.Set();
        quit.Dispose();
    }
    for (var i = 0; i < 40; i++)
    {
        await Task.Delay(500);
        if (engine.StartProvider())
        {
            return true;
        }
    }
    return false;
}

static void RemoveStartupEntry()
{
    using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
    if (k?.GetValue("Marea") is not null)
    {
        k.DeleteValue("Marea");
    }
}

string Arg(int i) => args.Length > i ? args[i] : throw new ArgumentException(Loc.T("cli.missing_arg"));

long GbArg()
{
    var i = Array.IndexOf(args, "--gb");
    return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var gb) ? (long)(gb * (1L << 30)) : 0;
}

void Status()
{
    foreach (var v in engine.Volumes())
    {
        Console.WriteLine(Loc.T("cli.volume", v.Volume, Gb(v.FreeBytes), Gb(v.TotalBytes), Gb(v.TargetBytes)));
    }
    var files = engine.Ledger.Files();
    var allowed = engine.Plan(0).Sum(p => p.File.Size);
    Console.WriteLine("\n" + Loc.T("cli.ledger", files.Count, Gb(files.Sum(f => f.Size))));
    foreach (var g in files.GroupBy(f => f.Class).OrderByDescending(g => g.Key))
    {
        Console.WriteLine($"  {Label(g.Key),-14} {g.Count(),4}  {Gb(g.Sum(f => f.Size)),10}");
    }
    var gone = files.Where(f => f.Residency == Residency.Evaporated).ToList();
    Console.WriteLine($"  {Loc.T("res.evaporated"),-14} {gone.Count,4}  {Gb(gone.Sum(f => f.Size)),10}");
    Console.WriteLine("\n" + Loc.T("cli.freeable", Gb(allowed)));
}

void PrintPlan(List<PlanItem> plan)
{
    foreach (var p in plan)
    {
        Console.WriteLine($"{p.File.SizeGb,8:0.00} GB  {Dur(p.RebuildSeconds),9}  {Label(p.File.Class),-13} {Short(p.File.Path)}");
    }
    Console.WriteLine(Loc.T("plan.total", Gb(plan.Sum(p => p.File.Size)), plan.Count));
}

static string Label(Recoverability r) => Loc.T("class." + r.ToString().ToLowerInvariant());
static string Gb(long b) => $"{b / (double)(1L << 30):0.0} GB";
static string Dur(double s) => double.IsInfinity(s) ? "-" : s < 90 ? $"{s:0} s" : $"{s / 60:0} min";
static string Short(string p) => p.Length > 70 ? "..." + p[^67..] : p;
