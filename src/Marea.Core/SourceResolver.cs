namespace Marea.Core;

/// <summary>Finds and records where each content can be obtained from, then classifies files.
/// Every source is a proof (equal sha256, or a recipe that rebuilt the exact bytes).</summary>
public sealed class SourceResolver(Ledger ledger, MareaSettings settings, HuggingFaceClient hub)
{
    /// <summary>Refreshes Hub matches, mirror presence and recipes, then reclassifies every file.</summary>
    public async Task RefreshAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var files = ledger.Files().Where(f => f.Sha256.Length == 64).ToList();
        var shas = files.Select(f => f.Sha256).ToHashSet();

        foreach (var repo in settings.HuggingFaceRepos)
        {
            progress?.Report(new ScanProgress(Loc.T("phase.hub"), repo, 0, 0));
            try
            {
                foreach (var hf in await hub.ListLfsFilesAsync(repo, ct))
                {
                    if (shas.Contains(hf.Sha256))
                    {
                        ledger.PutSource(new Source(hf.Sha256, SourceKind.HuggingFace, hf.Locator, hf.Size, DateTime.UtcNow));
                    }
                }
            }
            catch (Exception e) when (e is HttpRequestException or ArgumentException or System.Text.Json.JsonException or TaskCanceledException)
            {
                ledger.Log("j.source_unreachable", repo, 0, false, e.Message);
            }
        }

        if (settings.MirrorRoot is { Length: > 0 } mirrorRoot && Directory.Exists(mirrorRoot))
        {
            var mirror = new MirrorStore(mirrorRoot);
            foreach (var f in files.Where(f => mirror.Has(f.Sha256, f.Size)))
            {
                ledger.PutSource(new Source(f.Sha256, SourceKind.Mirror, mirror.ObjectPath(f.Sha256), f.Size, DateTime.UtcNow));
            }
        }

        await FindRecipesAsync(files, progress, ct);

        foreach (var f in ledger.Files())
        {
            (f.Class, f.SourceLabel) = Classify(f);
            ledger.Upsert(f);
        }
    }

    /// <summary>Tries to express unsourced safetensors files with tensors of the other local files.</summary>
    private async Task FindRecipesAsync(List<FileRecord> files, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var local = files.Where(f => f.Residency == Residency.Local && f.Path.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)).ToList();
        var parsed = local.Select(f => SafetensorsFile.TryRead(f.Path)).Where(x => x is not null).Cast<SafetensorsFile>().ToList();
        foreach (var f in local)
        {
            if (ledger.GetRecipe(f.Sha256) is { } existing && Recipe.FromJson(existing).SourcesUnchanged())
            {
                continue;
            }
            if (ledger.SourcesOf(f.Sha256).Any(s => s.Kind != SourceKind.Recipe) || ledger.WithSha(f.Sha256).Count > 1)
            {
                continue; // cheaper sources already exist
            }
            var target = parsed.FirstOrDefault(p => string.Equals(p.Path, f.Path, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                continue;
            }
            var recipe = RecipeBuilder.TryBuild(target, f.Sha256, parsed);
            if (recipe is null)
            {
                continue;
            }
            var p = new Progress<long>(b => progress?.Report(new ScanProgress(Loc.T("phase.recipe"), f.Path, b, f.Size)));
            if (await RecipeBuilder.VerifyAsync(recipe, p, ct))
            {
                ledger.PutRecipe(f.Sha256, recipe.ToJson());
                ledger.PutSource(new Source(f.Sha256, SourceKind.Recipe, f.Sha256, f.Size, DateTime.UtcNow));
                ledger.Log("j.recipe_verified", f.Path, f.Size, true, recipe.Describe());
            }
        }
    }

    /// <summary>Best recoverability of a file given what is on disk right now.</summary>
    public (Recoverability, string) Classify(FileRecord f)
    {
        var copies = ledger.WithSha(f.Sha256).Where(o => !string.Equals(o.Path, f.Path, StringComparison.OrdinalIgnoreCase)
                                                        && o.Residency == Residency.Local).ToList();
        if (copies.Count > 0)
        {
            return (Recoverability.Duplicate, Loc.Code("src.copy", copies[0].Path));
        }
        var sources = ledger.SourcesOf(f.Sha256);
        if (sources.FirstOrDefault(s => s.Kind == SourceKind.Recipe) is not null
            && ledger.GetRecipe(f.Sha256) is { } json && Recipe.FromJson(json) is { } r && r.SourcesUnchanged())
        {
            return (Recoverability.Derivable, r.Describe());
        }
        if (sources.FirstOrDefault(s => s.Kind == SourceKind.Mirror) is { } m)
        {
            return (Recoverability.Reobtainable, Loc.Code("src.mirror", m.Locator));
        }
        if (sources.FirstOrDefault(s => s.Kind == SourceKind.HuggingFace) is { } h)
        {
            var hf = HubFile.Parse(h.Locator, h.Sha256, h.Size);
            return (Recoverability.Reobtainable, Loc.Code("src.hf", $"{hf.Repo}/{hf.Path}"));
        }
        return (Recoverability.Unique, Loc.Code("src.none"));
    }
}
