# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

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
