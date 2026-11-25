# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-11-25

Field-tested on a real workstation: 63.7 GB of ACE-Step / Side-Step models scanned in 65 s,
19.9 GB evaporated in 11 s, read back by `safetensors` + PyTorch bit-identical, then undone.

### Added
- **Automatic source discovery**: Hugging Face repositories are found from evidence already on
  disk (HF cache, README `base_model`, config `_name_or_path`, git remotes, Ollama `hf.co` models)
  and the same authors' related repositories. No hand-written list needed. Optional (off by
  default) search by folder name for the rest.
- **Ollama source**: blobs referenced by local manifests come back from `registry.ollama.ai`
  (or `hf.co`) by digest. Exact storage host pinned in the allow-list. The first-run guide proposes
  `.ollama\models\blobs`. Nothing changes for people who don't use Ollama.
- **Update notifications**: once a day the app asks GitHub for newer releases and only notifies.
- **Releases built by CI with build-provenance attestation** (`gh attestation verify`).
- `mareactl evaporate <file> --yes` to evaporate a single file.
- **"Free up space in 3 clicks"** wizard: choose how much (10 GB, 50 GB, up to the target,
  everything safe) and how files must come back (*instant only*: duplicates and recipes, offline
  in seconds; or also NAS / internet). A one-sentence summary replaces the file table.
- **One-click undo** of a cleanup: every evaporation is a batch; *Undo this cleanup* (wizard),
  *Undo last cleanup* (overview) and `mareactl undo` bring back exactly that batch.
- `mareactl free [--gb N|--all] [--instant] --yes`.
- Tray menu entry for the wizard.

### Fixed
- Recipes when two local models share tensor names (e.g. fp32 *sft* and bf16 *turbo*): every
  candidate model is tried on its own, with a fast spot check before the full verification.
- The plan never takes both a recipe target and the files it is rebuilt from (the safety gate
  already blocked it at execution time, but the plan overstated the space to be freed).

### Changed
- bf16 ← fp32 recomputation uses 256-bit SIMD and pooled buffers: 9.3 GB rebuilt in 35 s instead of 135 s.
- Default list of Hugging Face repositories is now empty (discovery replaces it).
- Gate verdicts and plan items now carry the return path (local copy, recipe, mirror, download).
- Ledger schema: new `batch` column, migrated automatically on existing ledgers.

## [0.1.0] - 2026-11-25

First public pre-release.

### Added
- Provenance ledger with four recoverability classes: duplicate, derivable, re-obtainable, unique.
- Sources: local duplicates, Hugging Face LFS files pinned to a commit, content-addressed
  mirror (NAS), bit-exact recipes (bf16 recomputed from fp32 shards).
- Safety gate, last-moment proof under an exclusive handle, verification of every returned file,
  refusal to detach folders that still hold placeholders.
- Transparent evaporation and return through the Windows Cloud Files API (no admin rights).
- Tide planner (expected cost of return × recency) with optional automatic mode, off by default.
- Desktop app: overview, files with per-file explanation, managed folders, activity journal,
  settings, first-run guide that finds model folders, plan confirmation, tray icon.
- `mareactl` command line, including `restore-all` for uninstallers.
- Five languages: English, Italian, Spanish, French, German.
- Portable build, per-user MSI authoring (WiX v7), winget templates, CI, CodeQL, Dependabot.
