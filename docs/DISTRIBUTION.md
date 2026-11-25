# Distributing Marea

## Build outputs

| Output | How | Notes |
|---|---|---|
| `dist/Marea/` | `build/publish.ps1` | App + `mareactl`, .NET 10 included (shared runtime, ~181 MB on disk) |
| `Marea-<v>-win-x64-portable.zip` | same | ~72 MB. Before deleting the folder: *Settings → Bring everything back and detach* |
| `Marea-<v>-win-x64-<culture>.msi` | `build/installer.ps1` | **Per-user** MSI (no UAC) in `%LOCALAPPDATA%\Programs\Marea`. Uninstall runs `mareactl restore-all --yes --uninstall` first and rolls back if it fails |

Every artefact ships with a `.sha256` file.

## Installer: why WiX v7

- **MSI with rollback**: if `restore-all` fails (e.g. not enough space to bring files back), the
  uninstall rolls back and no file is ever stranded. *Velopack* (30-second hooks, cannot abort)
  and *MSIX* (Windows detaches the sync root itself and runs no code at uninstall) were ruled out
  because, with Cloud Files, they could delete evaporated files.
- **License**: since v6 WiX has the *Open Source Maintenance Fee*, which applies only to users with
  US$10,000+ yearly revenue from activities using WiX. The EULA must still be accepted once by
  whoever builds: `dotnet wix eula accept wix7`.
- **Simpler alternative**: Inno Setup 7 (`PrivilegesRequired=lowest`) with `InitializeUninstall`
  calling `mareactl restore-all` and returning False on failure.

## Code signing (SmartScreen)

Unsigned builds show "Windows protected your PC" until they build reputation. Options for an
individual developer in the EU:

| Option | Cost | Constraints |
|---|---|---|
| **SignPath Foundation** | free | Open source with an OSI license and verifiable CI builds; publisher shown as "SignPath Foundation" |
| **Certum Open Source Code Signing** (cloud) | ~€49 | Certificate in your name, open source projects only |
| Azure **Artifact Signing** (formerly Trusted Signing) | from $9.99/month | Individuals: US/Canada only; EU organisations eligible |

EV certificates no longer bypass SmartScreen; reputation grows with downloads.

### SignPath setup (prepared, waiting for approval)

Everything on the repository side is ready: [CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md),
[build/signpath/artifact-configuration.xml](../build/signpath/artifact-configuration.xml), and the
signing steps in [release.yml](../.github/workflows/release.yml), which stay skipped until they are
configured. The maintainer then:

1. Turns on two-factor authentication on GitHub (Settings → Password and authentication).
2. Applies at <https://signpath.org/apply> with the repository URL.
3. After approval, in SignPath: connects GitHub.com as trusted build system, creates the project
   `marea` with the signing policy `release-signing`, pastes the artifact configuration, and adds
   themselves as approver.
4. In GitHub → Settings → Secrets and variables → Actions: secret `SIGNPATH_API_TOKEN`, variable
   `SIGNPATH_ORGANIZATION_ID` (optionally `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_POLICY_SLUG`).
5. Pushes a tag. The release job waits (up to 2 hours) for the signing request to be approved in
   SignPath, checks every signature with `Get-AuthenticodeSignature`, then packages and publishes.

### SignPath setup (prepared, waiting for approval)

Everything on the repository side is ready: [CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md),
[build/signpath/artifact-configuration.xml](../build/signpath/artifact-configuration.xml), and the
signing steps in [release.yml](../.github/workflows/release.yml), which stay skipped until they are
configured. The maintainer then:

1. Turns on two-factor authentication on GitHub (Settings → Password and authentication).
2. Applies at <https://signpath.org/apply> with the repository URL (the text is in the launch kit).
3. After approval, in SignPath: connects GitHub.com as trusted build system, creates the project
   `marea` with the signing policy `release-signing`, pastes the artifact configuration, and adds
   themselves as approver.
4. In GitHub → Settings → Secrets and variables → Actions: secret `SIGNPATH_API_TOKEN`, variable
   `SIGNPATH_ORGANIZATION_ID` (optionally `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_POLICY_SLUG`).
5. Pushes a tag. The release job waits (up to 2 hours) for the signing request to be approved in
   SignPath, checks every signature with `Get-AuthenticodeSignature`, then packages and publishes.

## winget

Manifest schema 1.12.0; a per-user MSI is accepted (`Scope: user`). Templates live in
`installer/winget/`: fill in URL, SHA-256 and ProductCode after a release. The uninstaller must
exit 0 even when there is nothing to bring back — `restore-all` does.

## Release checklist

1. `dotnet test tests/Marea.Tests` (with network), all green.
2. `dotnet list package --vulnerable --include-transitive` (the build already fails on known vulnerabilities).
3. `build/publish.ps1 -Version X.Y.Z`, then `build/installer.ps1 -Version X.Y.Z`.
4. On a clean machine: install, evaporate a test file, read it, uninstall, check the file is ordinary again.
5. Sign (when available), publish the GitHub release with `.sha256` files, update winget.
