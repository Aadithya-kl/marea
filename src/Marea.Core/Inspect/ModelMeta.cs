using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marea.Core;

/// <summary>What loading a file could do, from the safest to the most worrying.</summary>
/// <remarks>Ordered so that "worst of" is simply the maximum. <see cref="NotAModel"/> sits below
/// everything: a data file next to a model does not make the model look better or worse.</remarks>
public enum SafetyVerdict
{
    /// <summary>Not a recognised model format (data, text, unknown): nothing to judge.</summary>
    NotAModel = 0,
    /// <summary>A format that cannot run code when loaded (safetensors, GGUF, ONNX).</summary>
    SafeFormat = 1,
    /// <summary>Pickle-based, but it only rebuilds tensors with standard PyTorch/NumPy calls.</summary>
    NoCodeFound = 2,
    /// <summary>Imports something outside the standard set, contains code, or could not be read fully.</summary>
    Suspicious = 3,
    /// <summary>Would run commands, open files or the network, or evaluate code when loaded.</summary>
    Dangerous = 4,
}

/// <summary>What Marea learned by reading a file's header (never by loading it).</summary>
/// <param name="Format">Format name: GGUF, safetensors, PyTorch, TorchScript, pickle, ONNX, or data.</param>
/// <param name="Architecture">Model architecture when the file declares it (e.g. llama, qwen2).</param>
/// <param name="Name">Model name when the file declares it.</param>
/// <param name="Precision">Quantization or dominant tensor type (e.g. Q4_K_M, BF16).</param>
/// <param name="Parameters">Number of weights, 0 when unknown.</param>
/// <param name="Safety">Verdict of the safety check.</param>
/// <param name="Findings">What the check found: imports, code, template patterns. Untrusted text.</param>
public sealed record ModelMeta(
    string Format,
    string? Architecture,
    string? Name,
    string? Precision,
    long Parameters,
    SafetyVerdict Safety,
    IReadOnlyList<string> Findings)
{
    /// <summary>Bumped whenever the inspector learns something new, so old results are refreshed.</summary>
    public const int InspectorVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Metadata for a file that is not a model.</summary>
    public static ModelMeta Data { get; } = new("data", null, null, null, 0, SafetyVerdict.NotAModel, []);

    /// <summary>Serialises for the ledger.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads a ledger value; null when it is unreadable.</summary>
    public static ModelMeta? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ModelMeta>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
