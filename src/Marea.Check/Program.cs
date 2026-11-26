using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marea.Core;

// marea-check: is this model file safe to load? Reads headers and pickle opcodes only; it never
// loads, unpickles or executes anything. Same verdicts, JSON schema and exit codes as
// `mareactl check` on Windows. Exit: 0 ok, 3 check before loading, 4 dangerous, 64 usage.

string[] extensions = [".safetensors", ".sft", ".gguf", ".ckpt", ".pt", ".pth", ".bin", ".onnx", ".pkl", ".pickle", ".joblib"];
var json = args.Contains("--json");
var targets = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();

if (args.Contains("--version"))
{
    Console.WriteLine("marea-check " + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?"));
    return 0;
}
if (targets.Count == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        marea-check <file|folder>... [--json]

        Checks AI model files for code that would run when they are loaded, without loading them.
        Pickle-based files (.ckpt .pt .pth .bin .pkl) are rated by what they would import;
        TorchScript code and GGUF chat templates that try to escape the Jinja sandbox are flagged.

        Verdicts: safe_format (cannot run code), no_code_found (pickle that only rebuilds tensors),
                  suspicious (check before loading), dangerous (do not load), not_a_model.
        Exit codes: 0 ok, 3 check before loading, 4 dangerous, 64 usage.
        https://github.com/indecenti/marea
        """);
    return targets.Count == 0 && !args.Contains("--help") && !args.Contains("-h") ? 64 : 0;
}

var files = new List<string>();
foreach (var t in targets.Select(Path.GetFullPath))
{
    if (Directory.Exists(t))
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        files.AddRange(Directory.EnumerateFiles(t, "*", opts).Where(f =>
            extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
            || Path.GetFileName(f).StartsWith("sha256-", StringComparison.OrdinalIgnoreCase)));
    }
    else if (File.Exists(t))
    {
        files.Add(t);
    }
    else
    {
        Console.Error.WriteLine("Not found: " + Clean(t));
        return 64;
    }
}

var results = files.Select(f => (Path: f, Meta: ModelInspector.Inspect(f))).ToList();
var worst = results.Select(r => r.Meta.Safety).DefaultIfEmpty(SafetyVerdict.NotAModel).Max();
if (json)
{
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
    var doc = new
    {
        schema = "marea.check/1",
        worst,
        files = results.Select(r => new
        {
            path = r.Path,
            format = r.Meta.Format,
            architecture = r.Meta.Architecture,
            name = r.Meta.Name,
            precision = r.Meta.Precision,
            parameters = r.Meta.Parameters,
            safety = r.Meta.Safety,
            findings = r.Meta.Findings,
        }),
    };
    Console.WriteLine(JsonSerializer.Serialize(doc, options));
}
else
{
    foreach (var (path, m) in results)
    {
        Console.WriteLine($"{Label(m.Safety),-22} {m.Format,-12} {m.Precision,-8} {Clean(path)}");
        foreach (var f in m.Findings)
        {
            Console.WriteLine($"    - {Clean(f)}");
        }
    }
    Console.WriteLine($"\n{results.Count} files checked. Worst: {Label(worst)}.");
}
return worst switch
{
    SafetyVerdict.Dangerous => 4,
    SafetyVerdict.Suspicious => 3,
    _ => 0,
};

static string Label(SafetyVerdict v) => v switch
{
    SafetyVerdict.SafeFormat => "Safe format",
    SafetyVerdict.NoCodeFound => "No code found",
    SafetyVerdict.Suspicious => "Check before loading",
    SafetyVerdict.Dangerous => "Dangerous",
    _ => "Not a model",
};

// File names and model metadata are untrusted: no control, escape or bidi characters on the terminal.
static string Clean(string s)
{
    var sb = new StringBuilder(s.Length);
    foreach (var c in s)
    {
        var bad = char.IsControl(c) || c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '‎' or '‏';
        sb.Append(bad ? '?' : c);
    }
    return sb.ToString();
}
