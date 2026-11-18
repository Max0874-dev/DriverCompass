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

## Stage 3 – install (part 1 ✅: Windows Update drivers)

Done:

- "Install …" for drivers that the version check found on Windows Update; one driver per confirmation, disclaimer on first use.
- Separate UAC-elevated process (`DriverCompass.exe --elevated`) that re-checks device and update, creates a restore point (or asks again), installs via the Windows Update Agent, and logs the new `oemNN.inf` packages.
- History with rollback (`pnputil /delete-driver oemNN.inf /uninstall`), bound to the protected log and SHA-256.

Part 2 (open): OEM packages from Dell/Lenovo/HP – download over HTTPS with SHA-256 and Authenticode check, extract, install INF drivers with pnputil. Original plan for reference:

- Exactly one driver per user confirmation; no bulk installation.
- Download only via HTTPS from allow-listed domains.
- Afterwards: SHA-256 check (where the source provides a hash) and Authenticode/catalog signature check.
- The elevated helper (`DriverCompass.exe --elevated`, UAC "runas") runs only for this step.
  - It re-verifies everything itself instead of trusting the unelevated app.
  - It creates a restore point. If that fails (System Restore off, or only one point per 24 h), it shows a clear warning with an option to cancel.
  - It installs with `pnputil /add-driver <inf> /install`.
- Install log (JSON) and rollback via `pnputil /delete-driver oemNN.inf /uninstall`.
