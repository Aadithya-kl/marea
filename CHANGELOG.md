# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **"Free up space in 3 clicks"** wizard: choose how much (10 GB, 50 GB, up to the target,
  everything safe) and how files must come back (*instant only*: duplicates and recipes, offline
  in seconds; or also NAS / internet). A one-sentence summary replaces the file table.
- **One-click undo** of a cleanup: every evaporation is a batch; *Undo this cleanup* (wizard),
  *Undo last cleanup* (overview) and `mareactl undo` bring back exactly that batch.
- `mareactl free [--gb N|--all] [--instant] --yes`.
- Tray menu entry for the wizard.

### Changed
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
