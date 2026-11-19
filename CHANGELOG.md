# Changelog

## [Unreleased]

## [0.3.1] – 2026-11-19

**Preview:** the EXE is still not code-signed, so Windows SmartScreen will warn ("Windows protected your PC" → *More info* → *Run anyway*). Check the SHA-256 against `SHA256SUMS-*.txt` before running it. Code signing by SignPath Foundation is being applied for, see the [code signing policy](https://github.com/Max0874-dev/DriverCompass/blob/main/docs/code-signing-policy.md).

### Added
- The release ZIP now contains the full license and notice texts of every shipped third-party component (folder `licenses/`). THIRD-PARTY-NOTICES.md corrects the license of the json-everything packages (JsonSchema.Net): MIT source, binaries with the "Open Source Maintenance Fee" EULA, which does not apply to a free, non-commercial app like DriverCompass.

### Changed
- If the online vendor list ever needs a newer DriverCompass version, the app now says so ("please update") instead of reporting that the list failed validation. A test makes sure every schema change comes with a new schema version (see docs/adding-vendors.md).

### Download
- `DriverCompass-0.3.1-win-x64.zip` / `-win-arm64.zip`: unzip and run `DriverCompass.exe` (self-contained, no installation, Windows 10 1809+ / Windows 11). The license texts are in the folder `licenses`.

## [0.3.0] – 2026-11-19

**Preview:** the EXE is still not code-signed, so Windows SmartScreen will warn ("Windows protected your PC" → *More info* → *Run anyway*). Check the SHA-256 against `SHA256SUMS-*.txt` before running it. Code signing by SignPath Foundation is being applied for, see the [code signing policy](https://github.com/Max0874-dev/DriverCompass/blob/main/docs/code-signing-policy.md).

### Added
- Privacy notice on first start: before DriverCompass connects to anything, it explains what leaves the PC and lets you switch off the vendor list download and switch on the update hint. It is shown once, also to users updating from an older version.

### Changed
- A changed setting now applies to the running session even if the settings file cannot be written, so "do not download the vendor list" is never ignored.

### Download
- `DriverCompass-0.3.0-win-x64.zip` / `-win-arm64.zip`: unzip and run `DriverCompass.exe` (self-contained, no installation, Windows 10 1809+ / Windows 11).

## [0.2.0] – 2026-11-19

**Preview:** the EXE is still not code-signed, so Windows SmartScreen will warn ("Windows protected your PC" → *More info* → *Run anyway*). Check the SHA-256 against `SHA256SUMS-*.txt` before running it.

### Added
- Optional update hint (off by default): turn on *Settings → Check for a new DriverCompass version at startup*. DriverCompass then asks the public GitHub API for the latest release and, if it is newer, shows a link to the release page. Only the version number is read from the answer; the link is built from the app's own repository address. Nothing is downloaded or installed automatically, no device data is sent.

### Fixed
- PRIVACY.md and SECURITY.md showed some folder paths without backslashes.

### Download
- `DriverCompass-0.2.0-win-x64.zip` / `-win-arm64.zip`: unzip and run `DriverCompass.exe` (self-contained, no installation, Windows 10 1809+ / Windows 11).

## [0.1.1] – 2026-11-18

Small fix release. **Preview:** the EXE is still not code-signed, so Windows SmartScreen will warn ("Windows protected your PC" → *More info* → *Run anyway*). Check the SHA-256 against `SHA256SUMS-*.txt` before running it.

### Fixed
- When the vendor list cannot be updated, the warning now names the list that is actually used: the built-in one or the last downloaded one. Before, it always said "the saved version is used".

### Changed
- The repository is public now, so the app updates its vendor list from GitHub at startup (a plain download of a public file, no device data).

### Download
- `DriverCompass-0.1.1-win-x64.zip` / `-win-arm64.zip`: unzip and run `DriverCompass.exe` (self-contained, no installation, Windows 10 1809+ / Windows 11).

## [0.1.0] – 2026-11-18

First release. **Preview:** the EXE is not code-signed yet, so Windows SmartScreen will warn ("Windows protected your PC" → *More info* → *Run anyway*). Check the SHA-256 against `SHA256SUMS-*.txt` before running it.

### Detect & link (stage 1)
- Scans all Plug-and-Play devices (WMI, no admin rights) and shows missing, failing or generic drivers – problems first.
- Simple traffic-light view with "Get driver" and an expert table with search, filter, sort and HTML/CSV export; German and English.
- Official driver links: PC/mainboard manufacturer (with model or service tag where possible) → chip vendor → Microsoft Update Catalog.
- Vendor list for 17 PC/mainboard makers and 29 chip/peripheral vendors (incl. Lian Li hubs by exact USB product ID), validated before use, updated from GitHub (falls back to the built-in copy).

### Version check (stage 2, optional)
- "Check versions" compares installed drivers with Windows Update and the official Dell, Lenovo and HP catalogs. Every source is off by default and states what it sends where.
- Without an official source DriverCompass honestly says "Version unknown".

### Installation (stage 3, optional)
- "Install …" installs exactly one confirmed driver from Windows Update, Dell, HP or Lenovo – disclaimer on first use, restore point first, administrator rights only for that step (separate UAC-elevated process).
- Dell/HP packages are extracted without running vendor code; Lenovo packages are extracted by Lenovo's signed program (explicitly stated in the dialog). Only the signed INF driver is installed.
- Install history with rollback (pnputil).

### Download
- `DriverCompass-0.1.0-win-x64.zip` / `-win-arm64.zip`: unzip and run `DriverCompass.exe` (self-contained, no installation, Windows 10 1809+ / Windows 11).
- `--mock` shows example devices without touching your system.

See [PRIVACY.md](PRIVACY.md) and [SECURITY.md](SECURITY.md).
