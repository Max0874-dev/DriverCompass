# Release checklist – what is still missing for a public release

## Before going public

- [x] Repository URL set to `Max0874-dev/DriverCompass` in `Directory.Build.props` and `.github/ISSUE_TEMPLATE/config.yml`.
- [x] GitHub repository created, `main` pushed and made public (2026-11-18), so the app loads `data/vendors.json` online.
- [x] "Private vulnerability reporting" enabled (Settings → Security), so the security link in the issue templates works.
- [ ] **Code signing.** Unsigned EXEs trigger SmartScreen warnings. Chosen: [SignPath Foundation](https://signpath.org/), free for OSS. Policy, workflow and artifact configuration are ready; the application and the SignPath setup are described in [code-signing.md](code-signing.md). The release workflow signs as soon as `SIGNPATH_API_TOKEN` and `SIGNPATH_ORGANIZATION_ID` exist.
- [x] **Privacy notice during first start** – SignPath's terms ask that data sent to systems the user did not choose is shown "during installation" with an option to turn it off. DriverCompass has no installer, so a first-run notice covers this (the vendor list download is on by default).
- [ ] **MSIX** (optional). The manifest template is ready (`src/DriverCompass.App/Package.appxmanifest.template`). `Publisher` must match the certificate subject. CI builds the MSIX when the secrets `SIGNING_CERT` (base64 PFX) and `SIGNING_CERT_PASSWORD` exist.
- [x] **Update hint.** Opt-in check of the GitHub Releases API that only shows a link (no automatic download). Real auto-update could later come via MSIX App Installer or winget.
- [x] **Disclaimer** shown in the app before the first installation (must be ticked). Also add it to the README before a public release.
- [x] **License texts** of all shipped components are in `licenses/` and copied into the release ZIP; `LicenseTextTests` fails when a new package ships without them.
- [ ] Verify all vendor URLs again (some sites answer 403 to scripts; open them in a browser).
- [ ] winget manifest (`winget-pkgs` PR) once signed releases exist.

## Every release

1. Raise `<Version>` in `Directory.Build.props`.
2. `dotnet test --solution DriverCompass.slnx -p:Platform=x64`.
3. Run `scripts/update-pci-ids.ps1` if needed.
4. Commit, then tag `vX.Y.Z` and push the tag. `release.yml` builds ZIPs (x64/ARM64) and SHA256SUMS into a **draft** release.
5. Check the draft release, add release notes, publish.
