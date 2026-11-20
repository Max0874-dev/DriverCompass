# Code signing policy

**Status:** releases are **not signed**. SignPath Foundation declined the first application on 2026-11-20 because the project is still too new and little known; it welcomes a new application once the project is more widely used. Until then Windows SmartScreen warns when you start them. Check the SHA-256 against `SHA256SUMS-*.txt` from the release page.

If SignPath Foundation accepts the project later, Windows releases will be signed as follows:

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## What is signed

- Only files built from this repository's source code: `DriverCompass.exe`, `DriverCompass.dll` and `DriverCompass.*.dll`.
- They are built by the [release workflow](../.github/workflows/release.yml) on GitHub-hosted runners from a version tag (`vX.Y.Z`). Nothing built on a personal computer is signed.
- Every signing request is approved manually by an approver (see below) before it is signed.
- Third-party libraries (NuGet packages, .NET and Windows App SDK runtime) are shipped as published by their authors and are not re-signed by this project. See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

## Team roles

| Role | Members |
|---|---|
| Authors (commit without further review) | [Max0874-dev](https://github.com/Max0874-dev) |
| Reviewers (review every change from others, e.g. pull requests) | [Max0874-dev](https://github.com/Max0874-dev) |
| Approvers (approve each signing request) | [Max0874-dev](https://github.com/Max0874-dev) |

All members use multi-factor authentication for GitHub and SignPath.

## Privacy

DriverCompass sends no telemetry. The network connections it makes, which data they contain and how to turn them off are described in the [privacy policy](../PRIVACY.md).

## System changes

DriverCompass only changes the system when you explicitly install a single driver: it shows a disclaimer first, creates a restore point (or warns if that is not possible), asks for administrator rights only for that step and lets you roll the driver back from the install history. There is no installer; deleting the folder removes the app.

## Reporting a problem

If you think a signed DriverCompass file violates this policy, please report it privately via *Security → Report a vulnerability* in this repository (see [SECURITY.md](../SECURITY.md)).
