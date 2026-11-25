# Marea — Security model

Marea moves gigabytes of the user's files in and out of the disk. The one thing it
must never do is lose data. This document lists what can go wrong and what stops it.

## Guarantees

1. **Nothing leaves the disk without an independent, verified way back.**
   The safety gate (`SafetyGate.cs`) allows evaporation only when, *after* the
   evaporation, at least one of these still exists:
   - another local copy with the same SHA-256, still on disk and unchanged;
   - a recipe that rebuilt the exact bytes (verified by hashing the full rebuild)
     whose source files stay on disk ("pinned");
   - a verified copy in the content-addressed mirror (e.g. a NAS);
   - a public Hugging Face file with the same SHA-256, pinned to a commit.
   Files with no source are **unique** and are never evaporated.
2. **Last-moment proof.** Right before evaporating, Marea opens the file with an
   **exclusive handle** (nobody can write it), re-hashes every byte, checks that a
   source answers *now* (HEAD request for the Hub, presence and size for the
   mirror, unchanged sources for recipes), refuses hard-linked files, and only then
   converts the file to a placeholder on the same handle.
3. **Every byte that comes back is verified.** After a file is fully hydrated it is
   hashed again. A mismatch is logged loudly, the file is evaporated again so it
   can be fetched from scratch, and the source that served it is distrusted.
4. **Windows never gets the chance to delete placeholders.**
   `CfUnregisterSyncRoot` deletes placeholders that are not hydrated. Marea detaches
   a folder only after a full walk of that folder proves it holds **only ordinary
   files** (`MareaEngine.ReleaseRoot`); otherwise it refuses and keeps the folder
   registered. A test covers the case where the ledger itself "forgets" a file.
5. **The user is always in control.** The first scan only measures. Evaporation
   needs an explicit confirmation of a visible plan, or the automatic tide, which is
   **off by default**. "Bring everything back" is one click (`mareactl restore-all`).
   It checks free space first and returns exit code 0 only when nothing is left
   evaporated.
6. **Uninstalling cannot strand files.** The installer runs
   `mareactl restore-all --yes --uninstall` *before* removing any file and aborts
   the uninstall if it fails.

## Attack surface

| Area | Exposure | Mitigation |
|---|---|---|
| Network | Outbound HTTPS only, to `huggingface.co` / `*.huggingface.co` / `*.hf.co` | Host allow-list enforced on every request **and every redirect**, HTTPS only, no cookies, no credentials, no inbound listener |
| Data sent | Public repository names (from settings) and requests for public files | Never local paths, file names or hashes of the user's files |
| Repo names | Taken from settings | Validated against `owner/name` pattern before use |
| Local IPC | Named events `Local\Marea.App.*` (show / quit) | Session-local; at worst another process of the same user can close Marea, which it could do anyway |
| Provider role | Lock file in `%LOCALAPPDATA%\Marea` | Released by Windows if the process dies |
| Privileges | None: per-user, no admin, no service, no driver | Uses the built-in cloud filter driver of Windows |
| Folders | Only user-chosen folders | Rejected: system folders, whole drives, profile roots, network/removable drives, non-NTFS, overlaps, folders owned by OneDrive or other sync engines |
| Ledger | SQLite in the user profile | WAL journal; append-only activity log |
| Dependencies | `Microsoft.Data.Sqlite`, `SQLitePCLRaw` (pinned past GHSA-2m69-gcr7-jv3q), `Microsoft.Windows.CsWin32` (build-time only) | `dotnet list package --vulnerable --include-transitive` in CI |
| Telemetry | None | — |

## Known limits

- Evaporated files open only while Marea runs (like OneDrive "online-only" files).
  The app offers to start with Windows; without it, programs get a "cloud provider
  not running" error (0x8007016A) until Marea starts.
- Opening a placeholder for reading (including an antivirus scan on open) brings
  the whole file back. That is by design: progressive hydration, verified at the end.
- Unsigned builds trigger SmartScreen. See `docs/DISTRIBUTION.md` for signing options.

## Reporting a vulnerability

Please use GitHub's **private vulnerability reporting** (Security tab → *Report a
vulnerability*) instead of a public issue. You will get an answer within a week.
