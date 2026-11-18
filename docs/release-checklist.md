# Release checklist – what is still missing for a public release

## Before going public

- [ ] **GitHub repository created** and `OWNER` replaced in `Directory.Build.props` and `.github/ISSUE_TEMPLATE/config.yml`. Without this the app only uses the built-in vendor list.
- [ ] **Code-signing certificate.** Unsigned EXEs trigger SmartScreen warnings. Options:
  - [SignPath Foundation](https://signpath.org/), free for OSS
  - Azure Trusted Signing, low monthly cost
  - a classic OV/EV certificate
- [ ] **MSIX** (optional). The manifest template is ready (`src/DriverCompass.App/Package.appxmanifest.template`). `Publisher` must match the certificate subject. CI builds the MSIX when the secrets `SIGNING_CERT` (base64 PFX) and `SIGNING_CERT_PASSWORD` exist.
- [ ] **Auto-update.** Not implemented. Simplest option: opt-in check of the GitHub Releases API, then show a link. Alternatives: MSIX App Installer, or winget.
- [ ] **Disclaimer.** Required before stage 3 (installing drivers): "no warranty, create a backup, drivers come from third parties". Show it in the app on first use and in the README.
- [ ] **Privacy notice** in the app. Currently it is in PRIVACY.md and the settings dialog; consider a first-run dialog.
- [ ] **License texts** of all NuGet dependencies included in the release ZIP (THIRD-PARTY-NOTICES.md plus the full texts).
- [ ] Verify all vendor URLs again (some sites answer 403 to scripts; open them in a browser).
- [ ] winget manifest (`winget-pkgs` PR) once signed releases exist.

## Every release

1. Raise `<Version>` in `Directory.Build.props`.
2. `dotnet test --solution DriverCompass.slnx -p:Platform=x64`.
3. Run `scripts/update-pci-ids.ps1` if needed.
4. Commit, then tag `vX.Y.Z` and push the tag. `release.yml` builds ZIPs (x64/ARM64) and SHA256SUMS into a **draft** release.
5. Check the draft release, add release notes, publish.
