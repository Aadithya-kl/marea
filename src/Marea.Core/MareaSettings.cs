using System.Text.Json;

namespace Marea.Core;

/// <summary>User settings, stored as JSON in %LOCALAPPDATA%\Marea\settings.json.</summary>
public sealed class MareaSettings
{
    /// <summary>Folders Marea is allowed to manage. Nothing outside them is ever touched.</summary>
    public List<string> ManagedRoots { get; set; } = [];

    /// <summary>Free space the tide keeps on each volume, in GiB.</summary>
    public double FreeSpaceTargetGb { get; set; } = 100;

    /// <summary>When true, the tide evaporates automatically below the target. Off by default.</summary>
    public bool AutoTide { get; set; }

    /// <summary>Files used or modified more recently than this are never evaporated.</summary>
    public int MinIdleDays { get; set; } = 7;

    /// <summary>Files smaller than this are ignored.</summary>
    public long MinFileSizeMb { get; set; } = 50;

    /// <summary>Public Hugging Face repositories checked as sources (owner/name).</summary>
    public List<string> HuggingFaceRepos { get; set; } =
    [
        "ACE-Step/Ace-Step1.5", "ACE-Step/acestep-5Hz-lm-0.6B", "ACE-Step/acestep-5Hz-lm-1.7B",
        "ACE-Step/acestep-5Hz-lm-4B", "ACE-Step/acestep-v15-turbo", "ACE-Step/acestep-v15-sft",
        "ACE-Step/acestep-v15-base", "ACE-Step/acestep-v15-xl-sft", "ACE-Step/acestep-v15-xl-turbo",
        "ACE-Step/acestep-v15-xl-base", "Qwen/Qwen3-Embedding-0.6B", "scragnog/Ace-Step-1.5-ScragVAE",
        "marcorez8/acestep-v15-xl-turbo-bf16",
    ];

    /// <summary>Optional mirror folder (e.g. a NAS share) for the content-addressed store.</summary>
    public string? MirrorRoot { get; set; }

    /// <summary>Download speed used to estimate re-download cost, in MB/s.</summary>
    public double BandwidthMBps { get; set; } = 50;

    /// <summary>Start the agent at sign-in (needed to bring evaporated files back).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>UI language code (it, en, es, fr, de) or "auto" to follow Windows.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>True once the first-run guide has been completed.</summary>
    public bool OnboardingDone { get; set; }

    /// <summary>Default location of the settings file.</summary>
    public static string DefaultDirectory =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marea");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Loads settings from <paramref name="dir"/>; returns defaults when absent or unreadable.</summary>
    public static MareaSettings Load(string dir)
    {
        var path = System.IO.Path.Combine(dir, "settings.json");
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<MareaSettings>(File.ReadAllText(path)) ?? new MareaSettings()
                : new MareaSettings();
        }
        catch (JsonException)
        {
            return new MareaSettings();
        }
    }

    /// <summary>Saves atomically (write temp, then replace).</summary>
    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "settings.json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
