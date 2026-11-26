using System.Collections.Concurrent;
using Marea.CloudFiles;

namespace Marea.Core;

/// <summary>A hydration in progress, for the UI.</summary>
/// <param name="Path">File coming back.</param>
/// <param name="Done">Bytes delivered.</param>
/// <param name="Total">File size.</param>
/// <param name="Source">Where the bytes come from.</param>
/// <param name="Finished">True on the last event.</param>
/// <param name="Error">Error, if it failed.</param>
public sealed record HydrationEvent(string Path, long Done, long Total, string Source, bool Finished, string? Error);

/// <summary>Serves Windows' requests for the data of evaporated files, from the cheapest live
/// source, and re-verifies the sha256 of every file once it is complete.</summary>
public sealed class HydrationService(Ledger ledger, HuggingFaceClient hub, OllamaRegistry ollama, ReaderGuard? guard = null)
{
    /// <summary>Raised the first time a program brings many evaporated files back in a short time.</summary>
    public event Action<MassRead>? MassRead;

    private const int Chunk = 4 << 20;
    private readonly ConcurrentDictionary<string, byte> _verifying = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised for progress and completion.</summary>
    public event Action<HydrationEvent>? Progress;

    /// <summary>Handles one fetch request for a file under <paramref name="root"/>.</summary>
    public async Task HandleAsync(FetchRequest req, string root)
    {
        var sha = ContentHash.FromIdentity(req.Identity);
        var path = Path.GetPathRoot(root)!.TrimEnd('\\') + req.Path;
        if (guard is not null && guard.IsBlocked(req.ProcessImage))
        {
            req.Fail(req.RequiredOffset, req.RequiredLength);
            var name = ReaderGuard.DisplayName(req.ProcessImage!);
            if (req.RequiredOffset == 0)
            {
                ledger.Log("j.reader_blocked", path, req.FileSize, false, name);
            }
            Progress?.Invoke(new HydrationEvent(path, 0, req.FileSize, "", true, Loc.T("hyd.blocked", name)));
            return;
        }
        if (guard?.Record(req.ProcessImage, path, req.FileSize) is { } alert)
        {
            ledger.Log("j.mass_read", alert.Process, alert.Bytes, false, Loc.Code("hyd.mass_read", alert.Name, alert.Files));
            MassRead?.Invoke(alert);
        }
        var start = req.RequiredOffset & ~4095L;
        var end = Math.Max(req.RequiredOffset + req.RequiredLength, req.OptionalFileOffset + req.OptionalLength);
        end = Math.Min(req.FileSize, (end + 4095) & ~4095L);
        string? lastError = null;
        foreach (var (label, open) in Candidates(sha, path))
        {
            IRangeSource? src = null;
            try
            {
                src = await open();
                if (src.Length != req.FileSize)
                {
                    lastError = Loc.T("hyd.size_differs", Loc.Render(label));
                    continue;
                }
                await PumpAsync(src, req, path, label, start, end);
                Progress?.Invoke(new HydrationEvent(path, end, req.FileSize, label, end == req.FileSize, null));
                if (end == req.FileSize)
                {
                    _ = VerifyWhenCompleteAsync(path, sha, label);
                }
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                lastError = $"{Loc.Render(label)}: {e.Message}";
            }
            finally
            {
                if (src is not null)
                {
                    await src.DisposeAsync();
                }
            }
        }
        req.Fail(req.RequiredOffset, req.RequiredLength);
        ledger.Log("j.return_failed", path, req.FileSize, false, lastError ?? Loc.Code("hyd.no_source"));
        Progress?.Invoke(new HydrationEvent(path, 0, req.FileSize, "", true, lastError ?? Loc.T("hyd.no_source")));
    }

    private async Task PumpAsync(IRangeSource src, FetchRequest req, string path, string label, long start, long end)
    {
        var buf = new byte[Chunk];
        var pos = start;
        while (pos < end)
        {
            req.Cancellation.ThrowIfCancellationRequested();
            var want = (int)Math.Min(Chunk, end - pos);
            var n = await src.ReadFullyAsync(pos, buf.AsMemory(0, want), req.Cancellation);
            if (n != want)
            {
                throw new IOException(Loc.T("hyd.source_stopped"));
            }
            req.Transfer(pos, buf.AsSpan(0, n));
            pos += n;
            req.ReportProgress(req.FileSize, pos);
            Progress?.Invoke(new HydrationEvent(path, pos, req.FileSize, label, false, null));
        }
    }

    /// <summary>Live sources, cheapest first.</summary>
    private IEnumerable<(string, Func<Task<IRangeSource>>)> Candidates(string sha, string path)
    {
        foreach (var copy in ledger.WithSha(sha).Where(o => !string.Equals(o.Path, path, StringComparison.OrdinalIgnoreCase)
                                                           && o.Residency == Residency.Local && Evaporator.StillAsRecorded(o)))
        {
            yield return (Loc.Code("src.local_copy"), () => Task.FromResult<IRangeSource>(new FileRangeSource(copy.Path)));
        }
        if (ledger.GetRecipe(sha) is { } json && Recipe.FromJson(json) is { } recipe && recipe.SourcesUnchanged())
        {
            yield return (recipe.Describe(), () => Task.FromResult<IRangeSource>(new RecipeRangeSource(recipe)));
        }
        foreach (var s in ledger.SourcesOf(sha).OrderBy(s => s.Kind is SourceKind.HuggingFace or SourceKind.Ollama ? 1 : 0))
        {
            if (s.Kind == SourceKind.Mirror && File.Exists(s.Locator))
            {
                yield return (Loc.Code("src.mirror_short"), () => Task.FromResult<IRangeSource>(new FileRangeSource(s.Locator)));
            }
            else if (s.Kind == SourceKind.Ollama)
            {
                var blob = OllamaBlob.Parse(s.Locator, s.Size);
                yield return (Loc.Code("src.ollama_short", blob.Repository), () => Task.FromResult(ollama.Open(blob)));
            }
            else if (s.Kind == SourceKind.HuggingFace)
            {
                var hf = HubFile.Parse(s.Locator, s.Sha256, s.Size);
                yield return (Loc.Code("src.hf_short", hf.Repo), () => Task.FromResult<IRangeSource>(new HubRangeSource(hub, hf)));
            }
        }
    }

    /// <summary>Once the whole file is on disk, proves it hashes to the expected value.</summary>
    private async Task VerifyWhenCompleteAsync(string path, string sha, string label)
    {
        if (!_verifying.TryAdd(path, 0))
        {
            return;
        }
        try
        {
            for (var i = 0; i < 600 && Placeholder.GetState(path) == PlaceholderState.Dehydrated; i++)
            {
                await Task.Delay(1000);
            }
            var actual = await ContentHash.Sha256FileAsync(path);
            var rec = ledger.Get(path);
            if (actual == sha)
            {
                if (rec is not null)
                {
                    rec.Residency = Residency.Local;
                    rec.EvaporatedUtc = null;
                    rec.LastAccessUtc = DateTime.UtcNow;
                    ledger.Upsert(rec);
                }
                ledger.Log("j.returned_verified", path, rec?.Size ?? 0, true, label);
                return;
            }
            ledger.Log("j.mismatch", path, rec?.Size ?? 0, false, Loc.Code("hyd.mismatch_detail", Loc.Render(label)));
            try
            {
                using var h = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Placeholder.Dehydrate(h);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CloudFilesException)
            {
                ledger.Log("j.action_needed", path, 0, false, Loc.Code("hyd.close_and_retry"));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ledger.Log("j.verify_postponed", path, 0, false, e.Message);
        }
        finally
        {
            _verifying.TryRemove(path, out _);
        }
    }
}
