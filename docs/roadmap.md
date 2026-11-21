# Roadmap

## Stage 1 – detect & link ✅

- WMI scan of all PnP devices, driver data, PC/mainboard identity
- Hardware ID parser (PCI, USB, HDAUDIO, ACPI, HID, Bluetooth, MONITOR …)
- Status: problem / driver missing / generic driver / turned off / OK
- Links: OEM (serial/model) → chip vendor → Microsoft Update Catalog
- Simple view and expert view, HTML/CSV export, German/English
- Vendor list: JSON + schema, online update with validation, cache, embedded fallback

## Stage 2 – compare versions ✅

- "Check versions" button; every source is opt-in in the settings and off by default.
- Sources (official, machine-readable, verified 2026-11-18):
  - Windows Update Agent API (driver search, no admin rights needed)
  - Dell Command | Update catalogs (signed CAB index + per-model CAB with SHA-256)
  - Lenovo System Update catalogs (per machine type, descriptors with SHA-256)
  - HP Image Assistant reference files (signed CABs per platform and Windows release)
- No HTML scraping. Without a source the app says "Version unknown – please check the link". Chip vendors (Intel, NVIDIA, AMD, Realtek …) have no official public interface, so they stay "unknown".
- Results in both views, a filter "Newer version available", detail pane and HTML/CSV export.
- Possible later: more OEMs with official catalogs, optional automatic check after the scan.

## Stage 3 – install ✅ (Windows Update, Dell, HP, Lenovo)

Done:

- "Install …" for drivers that the version check found on Windows Update; one driver per confirmation, disclaimer on first use.
- Separate UAC-elevated process (`DriverCompass.exe --elevated`) that re-checks device and update, creates a restore point (or asks again), installs via the Windows Update Agent, and logs the new `oemNN.inf` packages.
- History with rollback (`pnputil /delete-driver oemNN.inf /uninstall`), bound to the protected log and SHA-256.

Part 2 (done): Dell Update Packages and HP SoftPaqs.

- The elevated process looks the package up in the signed vendor catalog itself (protected cache), downloads it over HTTPS from `downloads.dell.com` / `ftp.hp.com`, checks SHA-256 and the Dell/HP Authenticode signer.
- The 7z payload appended to these EXEs is extracted with Windows' own `tar.exe` – vendor code is never run.
- Only INFs for this device, for this CPU architecture and with a validly signed catalog are installed with `pnputil /add-driver /install`; no vendor software or control panels.
- If Windows does not use the package for any device, exactly that package is removed again.

Part 3 (done): Lenovo packages.

- Lenovo packages are Inno Setup EXEs. The descriptor's standard extract command must match exactly; Lenovo's signed EXE (SHA-256 chain catalog → descriptor → EXE, signer "Lenovo") then extracts itself in the admin-only work folder with fixed arguments – the only place where vendor code runs elevated, as with Lenovo System Update. Installation as for Dell/HP: only the signed INF via pnputil.

Original plan for reference:

- Exactly one driver per user confirmation; no bulk installation.
- Download only via HTTPS from allow-listed domains.
- Afterwards: SHA-256 check (where the source provides a hash) and Authenticode/catalog signature check.
- The elevated helper (`DriverCompass.exe --elevated`, UAC "runas") runs only for this step.
  - It re-verifies everything itself instead of trusting the unelevated app.
  - It creates a restore point. If that fails (System Restore off, or only one point per 24 h), it shows a clear warning with an option to cancel.
  - It installs with `pnputil /add-driver <inf> /install`.
- Install log (JSON) and rollback via `pnputil /delete-driver oemNN.inf /uninstall`.

## Distribution

- GitHub releases: self-contained ZIP for x64 and ARM64 (all three stages). Not signed yet, see [code-signing.md](code-signing.md).
- Microsoft Store edition (published 2026-11-22, <https://apps.microsoft.com/detail/9PKVXM3Q7P7L>): stages 1 and 2 only, signed by Microsoft, updated by the Store. The Store accepts no app that needs administrator rights, so the Store edition is built without the installer. See [store-edition.md](store-edition.md).
- Later: winget manifest once signed releases exist.
