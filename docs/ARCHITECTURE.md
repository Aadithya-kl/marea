# Architecture

Marea is split so that the safety-critical logic is small, testable and platform-independent,
and the Windows-specific part is a thin layer.

```
src/
  Marea.CloudFiles/   Windows Cloud Files interop (cldapi.dll via CsWin32): sync roots,
                      placeholders (convert + dehydrate, hydrate, revert, state), provider
                      connection and FETCH_DATA callbacks.
  Marea.Core/         The engine. No UI.
  Marea.App/          WPF desktop app: views, first-run guide, tray icon, tide timer.
  Marea.Cli/          mareactl: same engine for scripts, SSH and the uninstaller.
tests/Marea.Tests/    Unit tests + end-to-end tests on the real cloud filter driver.
tools/                i18n table (single source for 6 languages), icon generator.
installer/            WiX v7 per-user MSI, winget manifest templates.
build/                publish.ps1 (portable), installer.ps1 (MSI).
```

## Marea.Core, file by file

| File | Responsibility |
|---|---|
| `Ledger.cs` | SQLite (WAL) provenance ledger: files, verified sources, recipes, append-only journal |
| `Scanner.cs` | Walks managed folders (never through junctions), hashes only what changed |
| `ContentHash.cs` | Streaming SHA-256; placeholder identity = raw content hash |
| `SourceResolver.cs` | Finds sources and classifies: duplicates, Hugging Face matches, mirror objects, recipes |
| `HuggingFace.cs` | Read-only Hub client: LFS listing pinned to a commit, HEAD verification, range reads; host allow-list on every redirect |
| `Safetensors.cs` | Header parser; bit-exact F32 → BF16 (round-to-nearest-even, torch semantics) |
| `Recipe.cs` | Builds, verifies (full rebuild + hash) and serves random-access reads of derivable files |
| `MirrorStore.cs` | Content-addressed store on a NAS: `marea-store/sha256/aa/<sha>`, verified copy then atomic rename |
| `SafetyGate.cs` | The rules: a file may leave only if an independent source survives; pinning of sources |
| `TidePlanner.cs` | Greedy plan by expected cost (use probability × rebuild seconds ÷ GB), re-checked per pick |
| `Evaporator.cs` | Last-moment proof under an exclusive handle, then convert/dehydrate |
| `HydrationService.cs` | Serves FETCH_DATA from the cheapest live source, re-hashes complete files |
| `MareaEngine.cs` | Facade: roots, scan, plan, evaporate, bring back, restore-all, provider role (lock file) |
| `Loc.cs` + `i18n/*.json` | Localization; persistent texts stored as codes and rendered at display time |
| `FileSystemFacts.cs`, `ModelFolderFinder.cs` | Folder validation rules, model-folder discovery |
| `Inspect/ModelInspector.cs` | Identifies a file by its bytes (GGUF, safetensors, PyTorch zip, raw pickle, ONNX) and returns its `ModelMeta` |
| `Inspect/GgufReader.cs` | Bounded GGUF v2/v3 header reader: architecture, name, quantization, parameter count, chat template |
| `Inspect/PickleScanner.cs`, `Inspect/ImportRating.cs` | Opcode walk of pickle streams (never unpickled) and the allow/deny lists that rate their imports; Jinja red flags for GGUF templates |
| `Inspect/ModelCatalog.cs`, `Inspect/OllamaNames.cs` | Groups files into models (HF cache, Ollama tags, model folders, shards, single files) |
| `LedgerMeta.cs` | Model metadata stored by content hash, versioned by inspector version |
| `Watch/SourceWatch.cs`, `Watch/SourceHealth.cs` | Daily source watch: probes, pure health rules (healthy, unreachable, at risk, gone), findings (rescue, protect, lost) |
| `Watch/HubWatch.cs`, `Watch/OllamaWatch.cs` | Probes: pinned Hub file HEAD, repository flags and latest revision, Ollama blob HEAD and tag manifests |
| `LedgerHealth.cs`, `MareaEngineWatch.cs` | Stored source health (a failed check never overwrites a conclusive state); rescue and protection |

## Life of a file

1. **Scan**: the file is hashed and recorded as *Unique*, *Local*.
2. **Sources**: the resolver finds that its hash equals a public Hub LFS object, so it adds a
   source `owner/repo@commit:path` and reclassifies the file as *Re-obtainable*.
3. **Plan**: the gate allows it (idle long enough, source independent); the planner ranks it.
4. **Evaporate**: exclusive handle, size/mtime check, full re-hash, HEAD to the Hub, then
   `CfConvertToPlaceholder(MARK_IN_SYNC | DEHYDRATE)` with the hash as identity. 0 bytes on disk.
5. **Read by any program**: Windows calls `FETCH_DATA`; the service picks the cheapest live
   source and streams the requested ranges with `CfExecute(TRANSFER_DATA)`.
6. **Verify**: when the file is complete it is re-hashed; the ledger marks it *Local* again.

## Windows specifics worth knowing

- Hydration policy is **progressive**; opening a placeholder for reading (including an
  antivirus scan on open) recalls the data.
- `CfUnregisterSyncRoot` **deletes** non-hydrated placeholders. `ReleaseRoot` therefore walks
  the whole folder and refuses if any placeholder remains.
- Only one process can be connected to a sync root. The provider role is a lock file that
  Windows releases if the process dies; `mareactl restore-all` asks the app to quit through a
  session-local event, then takes the role.
- In-sync tracking is `TRACK_ALL`: a placeholder edited by the user stops being in sync, can
  no longer be dehydrated, and is reverted to an ordinary file on the next scan.
