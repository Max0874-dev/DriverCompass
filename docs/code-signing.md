# Code signing with SignPath Foundation (maintainer notes)

Goal: sign the Windows binaries of every release so that SmartScreen and UAC show a verified publisher instead of "Unknown publisher". The public rules are in [code-signing-policy.md](code-signing-policy.md).

SignPath Foundation signs open source projects for free. The certificate is issued to **SignPath Foundation**, so Windows shows "SignPath Foundation" as the publisher, not your own name.

## 1. Before applying

- [ ] Turn on **two-factor authentication** for the GitHub account `Max0874-dev` (*Settings → Password and authentication*). SignPath requires MFA for everyone with access to the repository and to SignPath.
- [ ] The repository is public, MIT-licensed (OSI-approved) and has published releases (0.1.0, 0.1.1, 0.2.0). ✔
- [ ] [code-signing-policy.md](code-signing-policy.md) is linked from the README and from every release (the release workflow appends the link to the release notes). ✔
- [ ] The privacy policy describes every network connection and how to turn it off ([PRIVACY.md](../PRIVACY.md)). ✔

SignPath checks the project's reputation once, before it accepts it. A very new project with few users may be asked to come back later; there is no formal minimum.

## 2. Application (signpath.org/apply)

The form is filled in by the maintainer. Suggested answers:

| Question | Answer |
|---|---|
| Project name | DriverCompass |
| Repository | https://github.com/Max0874-dev/DriverCompass |
| License | MIT |
| Download page | https://github.com/Max0874-dev/DriverCompass/releases |
| Code signing policy | https://github.com/Max0874-dev/DriverCompass/blob/main/docs/code-signing-policy.md |
| Privacy policy | https://github.com/Max0874-dev/DriverCompass/blob/main/PRIVACY.md |
| Build system | GitHub Actions on GitHub-hosted runners (`.github/workflows/release.yml`), triggered by a version tag |
| What is signed | `DriverCompass.exe`, `DriverCompass.dll`, `DriverCompass.*.dll` inside the release ZIPs (x64 and ARM64) |

Short description:

> DriverCompass is a free, open-source Windows 10/11 app (C#/.NET, WinUI 3) that lists all devices, shows missing, failing or generic drivers and links to the manufacturer's official driver page. Optionally it compares driver versions with Windows Update and the official Dell, Lenovo and HP catalogs and installs a single driver the user confirms, in a separate UAC-elevated process with a restore point and rollback. No telemetry; every network connection is documented in PRIVACY.md and can be turned off.

Why signing matters for this project:

> The app asks for administrator rights for driver installation. A signed executable lets users check in the UAC prompt that the elevated process is really DriverCompass built from this repository.

## 3. After approval: set up SignPath

In the SignPath organization that SignPath Foundation creates for the project:

1. **Trusted build system:** add the predefined *GitHub.com* trusted build system and link it to the project. Install the *SignPath GitHub App* for the repository.
2. **Project:** slug `DriverCompass` (or note the slug SignPath gives you).
3. **Artifact configuration:** paste [`.signpath/artifact-configuration.xml`](../.signpath/artifact-configuration.xml) as the default configuration. `product-name` must match `<DriverCompassDisplayName>` in `Directory.Build.props`.
4. **Signing policy:** slug `release-signing`, with manual approval by the approver.
5. **API token:** create a token for a user with *submitter* permission on that project and policy.

## 4. Switch it on in GitHub

*Settings → Secrets and variables → Actions*:

| Kind | Name | Value |
|---|---|---|
| Secret | `SIGNPATH_API_TOKEN` | the API token |
| Variable | `SIGNPATH_ORGANIZATION_ID` | the organization ID (GUID) |
| Variable | `SIGNPATH_PROJECT_SLUG` | optional, default `DriverCompass` |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | optional, default `release-signing` |

Without these values the release workflow behaves exactly as before and publishes unsigned ZIPs.

With them, for each platform it:

1. uploads only the own binaries as a workflow artifact,
2. submits a signing request and waits until the approver approves it in SignPath (up to 60 minutes),
3. copies the signed files back, checks with `Get-AuthenticodeSignature` that every own binary has a valid signature from *SignPath Foundation*, and only then builds the ZIP and the checksums.

After the first signed release, update the status line in [code-signing-policy.md](code-signing-policy.md) and remove the SmartScreen note from the changelog template.

SmartScreen reputation is built per certificate over time. Right after the switch, a few users may still see a warning until enough downloads have been seen.
