# Roadmap

## Stage 1 – detect & link ✅ (current)

- WMI scan of all PnP devices, driver data, PC/mainboard identity
- Hardware ID parser (PCI, USB, HDAUDIO, ACPI, HID, Bluetooth, MONITOR …)
- Status: problem / driver missing / generic driver / turned off / OK
- Links: OEM (serial/model) → chip vendor → Microsoft Update Catalog
- Simple view and expert view, HTML/CSV export, German/English
- Vendor list: JSON + schema, online update with validation, cache, embedded fallback

## Stage 2 – compare versions (planned, not started)

- Each source is an `IVersionProvider` plugin (`DriverCompass.Core/Abstractions/IStage2VersionCheck.cs`).
- Only official, machine-readable sources. Candidates, each to be verified first:
  - Dell Command Update catalog (signed CAB/XML)
  - Lenovo System Update catalog (XML per machine type)
  - HP SoftPaq / HP Image Assistant reference files
  - Windows Update Agent API (driver search)
- No HTML scraping. Without a reliable source the app says "Version unknown, please check the link".
- ⚠ Windows Update and the per-model Lenovo catalog transmit device or model data. These providers set `TransmitsDeviceData = true` and must be **opt-in** per source.

## Stage 3 – download & install (planned, not started)

- Exactly one driver per user confirmation; no bulk installation.
- Download only via HTTPS from allow-listed domains.
- Afterwards: SHA-256 check (where the source provides a hash) and Authenticode/catalog signature check.
- `DriverCompass.Installer.exe` runs elevated (UAC "runas") only for this step.
  - It re-verifies everything itself instead of trusting the unelevated app.
  - It creates a restore point. If that fails (System Restore off, or only one point per 24 h), it shows a clear warning with an option to cancel.
  - It installs with `pnputil /add-driver <inf> /install`.
- Install log (JSON) and rollback via `pnputil /delete-driver oemNN.inf /uninstall`.
