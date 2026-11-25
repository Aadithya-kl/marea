using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marea.Cli;

/// <summary>Exit codes, stable for scripts and AI agents (documented in docs/AGENTS.md).</summary>
internal static class Exit
{
    /// <summary>Done.</summary>
    public const int Ok = 0;
    /// <summary>Failed (the message says why).</summary>
    public const int Error = 1;
    /// <summary>Not done: needs --yes, or the desktop app to close.</summary>
    public const int NeedsConfirmation = 2;
    /// <summary>check: something loading the file could run is unknown or unreadable. watch: models at risk.</summary>
    public const int Suspicious = 3;
    /// <summary>check: loading the file would run commands, open the network or evaluate code. watch: a model cannot come back.</summary>
    public const int Dangerous = 4;
    /// <summary>Refused: MAREA_READONLY=1 forbids changes.</summary>
    public const int ReadOnly = 5;
    /// <summary>Wrong command line.</summary>
    public const int Usage = 64;
}

/// <summary>Text and JSON output. File names and model metadata are untrusted: text output strips
/// control and bidi characters (no terminal escape injection), JSON escapes everything non-ASCII.</summary>
internal static class CliOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Writes one JSON document with a versioned schema name, e.g. "marea.models/1".</summary>
    public static void Json(string schema, object body)
    {
        var doc = new Dictionary<string, object?> { ["schema"] = schema };
        foreach (var p in JsonSerializer.SerializeToElement(body, Options).EnumerateObject())
        {
            doc[p.Name] = p.Value;
        }
        Console.WriteLine(JsonSerializer.Serialize(doc, Options));
    }

    /// <summary>Untrusted text made safe for a terminal: control, escape and bidi characters become '?'.</summary>
    public static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            var bad = char.IsControl(c) || c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '‎' or '‏';
            sb.Append(bad ? '?' : c);
        }
        return sb.ToString();
    }

    /// <summary>Path shortened for a terminal column, cleaned.</summary>
    public static string Short(string p)
    {
        var c = Clean(p);
        return c.Length > 70 ? "..." + c[^67..] : c;
    }

    /// <summary>Bytes as GB with one decimal.</summary>
    public static string Gb(long b) => $"{b / (double)(1L << 30):0.0} GB";

    /// <summary>A weight count as 7.2 B / 860 M.</summary>
    public static string Params(long n) => n <= 0 ? "-" : n >= 1_000_000_000 ? $"{n / 1e9:0.#} B" : $"{n / 1e6:0} M";
}
