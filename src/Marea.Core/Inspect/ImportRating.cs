namespace Marea.Core;

/// <summary>Rates what a pickle imports. Lists follow picklescan's spirit: a small allow-list of
/// tensor-rebuilding calls, and a deny-list of anything that reaches the OS, the network or eval.</summary>
public static class ImportRating
{
    private static readonly HashSet<string> DangerousModules = new(StringComparer.Ordinal)
    {
        "os", "posix", "nt", "subprocess", "sys", "socket", "shutil", "runpy", "webbrowser", "pty",
        "commands", "http", "httplib", "requests", "aiohttp", "urllib", "urllib2", "urllib3", "ftplib",
        "smtplib", "telnetlib", "socketserver", "pickle", "_pickle", "dill", "cloudpickle", "joblib",
        "importlib", "ctypes", "code", "codeop", "pdb", "bdb", "asyncio", "multiprocessing", "marshal",
        "pydoc", "venv", "pip", "types", "zipimport",
    };

    private static readonly HashSet<string> DangerousNames = new(StringComparer.Ordinal)
    {
        "builtins.eval", "builtins.exec", "builtins.execfile", "builtins.compile", "builtins.open",
        "builtins.getattr", "builtins.setattr", "builtins.delattr", "builtins.__import__", "builtins.apply",
        "builtins.breakpoint", "builtins.input", "builtins.globals", "builtins.locals", "builtins.vars",
        "operator.attrgetter", "operator.methodcaller", "functools.partial", "functools.reduce",
        "torch.serialization.load", "torch.load", "numpy.load", "numpy.testing._private.utils.runstring",
    };

    private static readonly HashSet<string> SafeNames = new(StringComparer.Ordinal)
    {
        "collections.OrderedDict", "collections.defaultdict", "torch._tensor._rebuild_from_type_v2",
        "torch.nn.parameter.Parameter", "torch.Size", "torch.device", "torch.dtype",
        "numpy.core.multiarray._reconstruct", "numpy.core.multiarray.scalar", "numpy._core.multiarray._reconstruct",
        "numpy._core.multiarray.scalar", "numpy.ndarray", "numpy.dtype", "_codecs.encode", "argparse.Namespace",
        "builtins.set", "builtins.frozenset", "builtins.slice", "builtins.bytearray", "builtins.complex", "builtins.range",
    };

    private static readonly HashSet<string> TorchDtypes = new(StringComparer.Ordinal)
    {
        "float32", "float16", "bfloat16", "float64", "int64", "int32", "int16", "int8", "uint8", "bool",
        "float8_e4m3fn", "float8_e5m2", "complex64", "complex128", "half", "float", "double", "long", "int",
    };

    /// <summary>Verdict for a list of imports (worst wins).</summary>
    public static SafetyVerdict Rate(IEnumerable<(string Module, string Name)> imports) =>
        imports.Select(i => Rate(i.Module, i.Name)).DefaultIfEmpty(SafetyVerdict.NoCodeFound).Max();

    /// <summary>Verdict for one import.</summary>
    public static SafetyVerdict Rate(string module, string name)
    {
        var mod = module == "__builtin__" ? "builtins" : module;
        var full = mod + "." + name;
        var root = mod.Split('.')[0];
        if (DangerousModules.Contains(root) || DangerousNames.Contains(full)
            || full.StartsWith("torch.hub", StringComparison.Ordinal) || full.StartsWith("torch._inductor", StringComparison.Ordinal)
            || full.StartsWith("torch.jit", StringComparison.Ordinal))
        {
            return SafetyVerdict.Dangerous;
        }
        if (SafeNames.Contains(full)
            || (mod == "torch._utils" && name.StartsWith("_rebuild", StringComparison.Ordinal))
            || (mod == "torch" && (name.EndsWith("Storage", StringComparison.Ordinal) || TorchDtypes.Contains(name)))
            || (mod == "numpy.dtypes" && name.EndsWith("DType", StringComparison.Ordinal)))
        {
            return SafetyVerdict.NoCodeFound;
        }
        return SafetyVerdict.Suspicious;
    }

    /// <summary>Jinja fragments that only appear in template-injection payloads, never in real chat templates.</summary>
    private static readonly string[] TemplateRed =
    [
        "__class__", "__globals__", "__subclasses__", "__init__", "__builtins__", "__import__", "__mro__",
        "__base__", "popen", "lipsum", "cycler", "joiner", "os.system", "subprocess",
    ];

    /// <summary>Patterns of a GGUF chat template that point at a Jinja sandbox escape.</summary>
    public static List<string> TemplateFindings(string? template) =>
        template is null ? [] : TemplateRed.Where(p => template.Contains(p, StringComparison.OrdinalIgnoreCase)).ToList();
}
