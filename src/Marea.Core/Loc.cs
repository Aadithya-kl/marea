using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Marea.Core;

/// <summary>Localization: one JSON file per language embedded in the assembly (i18n/xx.json),
/// English as fallback. Persistent texts (journal, source labels) are stored as codes
/// "key|arg|arg" and rendered at display time, so switching language re-renders history too.</summary>
public static class Loc
{
    private const char Sep = '|';
    private static readonly Dictionary<string, Dictionary<string, string>> Cache = [];
    private static Dictionary<string, string> _current = Load("en");
    private static readonly Dictionary<string, string> English = Load("en");

    /// <summary>Supported languages (code, native name).</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
        [("it", "Italiano"), ("en", "English"), ("es", "Español"), ("fr", "Français"), ("de", "Deutsch")];

    /// <summary>Active language code.</summary>
    public static string Language { get; private set; } = "en";

    /// <summary>Activates a language; "auto" (or unknown) follows Windows' display language, else English.</summary>
    public static void SetLanguage(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == "auto" || Languages.All(l => l.Code != code))
        {
            var sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            code = Languages.Any(l => l.Code == sys) ? sys : "en";
        }
        Language = code;
        _current = Load(code);
    }

    /// <summary>Translates <paramref name="key"/> and formats it with <paramref name="args"/>.</summary>
    public static string T(string key, params object?[] args)
    {
        var text = _current.GetValueOrDefault(key) ?? English.GetValueOrDefault(key) ?? key;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>Encodes a translatable message for storage: <c>key|arg|arg</c>.</summary>
    public static string Code(string key, params object?[] args) =>
        args.Length == 0 ? key : key + Sep + string.Join(Sep, args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)?.Replace(Sep, '¦')));

    /// <summary>Renders a stored code; plain text (e.g. an exception message) is returned unchanged.</summary>
    public static string Render(string? coded)
    {
        if (string.IsNullOrEmpty(coded))
        {
            return "";
        }
        var parts = coded.Split(Sep);
        if (!English.ContainsKey(parts[0]))
        {
            return coded;
        }
        return T(parts[0], parts.Skip(1).Cast<object?>().ToArray());
    }

    /// <summary>All keys of a language file (used by tests to keep translations complete).</summary>
    public static IReadOnlyCollection<string> Keys(string code) => Load(code).Keys;

    private static Dictionary<string, string> Load(string code)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(code, out var d))
            {
                return d;
            }
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Marea.Core.i18n.{code}.json");
            d = s is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? [];
            Cache[code] = d;
            return d;
        }
    }
}
