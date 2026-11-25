# Code signing policy

Windows releases of Marea are signed so that users can verify they come from this project.

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

> **Status:** the application to the SignPath Foundation program is being prepared. Until it is
> approved, releases are **not** code-signed; their integrity can be verified with the build
> provenance attestation (`gh attestation verify <file> --repo indecenti/marea`) and the
> `.sha256` file published with each release.

## What is signed

Only the files built from this repository:

- `Marea.exe`, `Marea.dll` (desktop app)
- `mareactl.exe`, `mareactl.dll` (command line)
- `Marea.Core.dll`, `Marea.CloudFiles.dll` (engine)

The .NET runtime and third-party libraries shipped next to them keep their original publishers'
signatures and are never signed with the project certificate. The exact list is in
[`build/signpath/artifact-configuration.xml`](build/signpath/artifact-configuration.xml).

## How a release is signed

1. A version tag (`v*`) is pushed to this repository.
2. The [release workflow](.github/workflows/release.yml) builds the app on GitHub-hosted runners
   from that tagged commit, runs the unit tests and uploads the unsigned build.
3. The workflow submits a signing request to SignPath. Every request is **approved manually** by an
   approver listed below, after checking that it comes from the expected tag and workflow run.
4. The signed files are packaged into the portable ZIP, checksummed, attested (Sigstore build
   provenance) and published as a GitHub release.

No binary built outside GitHub Actions, or from an untagged or modified commit, is ever signed.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [indecenti](https://github.com/indecenti) |
| Approvers | [indecenti](https://github.com/indecenti) |

All members use multi-factor authentication for GitHub and SignPath. Contributions from other
people are accepted only through pull requests reviewed by a committer.

## Privacy policy

Marea runs on your computer and has no telemetry, no analytics and no account. It does not send
your file names, paths, file contents or hashes anywhere.

It connects to the network only for the features below, each of which can be turned off in
*Settings*:

| Feature | Hosts | What is sent | Default |
|---|---|---|---|
| Finding public sources of your models | `huggingface.co`, `hf.co` | Names of public repositories and authors found on disk or entered by you | On |
| Search by folder name | `huggingface.co` | Names of your model folders | **Off** |
| Ollama models | `registry.ollama.ai` and its storage host | Public model names and content digests | On |
| Bringing an evaporated file back from a public source | the hosts above | A request for that public file | On demand |
| Daily check that public sources still publish your models | the hosts above | The same public names and digests | On |
| Update notification | `api.github.com` | A request for the latest Marea release | On |

Every request goes over HTTPS to an allow-listed host, checked again on every redirect. See
[SECURITY.md](SECURITY.md) for the full threat model.
