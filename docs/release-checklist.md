# Release checklist – what is still missing for a public release

## Before going public

- [x] Repository URL set to `Max0874-dev/DriverCompass` in `Directory.Build.props` and `.github/ISSUE_TEMPLATE/config.yml`.
- [x] GitHub repository created and `main` pushed (private). While it is private, the app cannot load `data/vendors.json` online and uses its cached or built-in list.
- [ ] Enable "Private vulnerability reporting" (Settings → Security) so the security link in the issue templates works.
- [ ] **Code-signing certificate.** Unsigned EXEs trigger SmartScreen warnings. Options:
  - [SignPath Foundation](https://signpath.org/), free for OSS
  - Azure Trusted Signing, low monthly cost
  - a classic OV/EV certificate
- [ ] **MSIX** (optional). The manifest template is ready (`src/DriverCompass.App/Package.appxmanifest.template`). `Publisher` must match the certificate subject. CI builds the MSIX when the secrets `SIGNING_CERT` (base64 PFX) and `SIGNING_CERT_PASSWORD` exist.
- [ ] **Auto-update.** Not implemented. Simplest option: opt-in check of the GitHub Releases API, then show a link. Alternatives: MSIX App Installer, or winget.
- [x] **Disclaimer** shown in the app before the first installation (must be ticked). Also add it to the README before a public release.
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
