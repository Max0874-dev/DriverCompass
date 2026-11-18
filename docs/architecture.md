# Architecture

```
            ┌────────────────────────── DriverCompass.App (WinUI 3, MVVM) ──────────────────────────┐
            │ MainWindow · SimpleView · ExpertView · SettingsDialog · ViewModels · Localizer (.resw) │
            └───────────────┬──────────────────────────────────────────────────────┬─────────────────┘
                            │ IDeviceAnalysisService                               │ IReportExporter
┌───────────────────────────▼──────────── DriverCompass.Core (net10.0, no Windows deps) ──────────────────────────┐
│ DeviceAnalysisService:  IDeviceScanner ─► IDeviceStatusEvaluator ─► ILinkResolver ─► sort (problems first)       │
│ HardwareIdParser · SystemDeviceFilter · SystemInfoSanitizer · VendorCatalog · UrlSafety · Html/Csv exporters     │
│ MockDeviceScanner · JsonSettingsService · interfaces for stage 2 (IVersionProvider) and 3 (installer, verifier) │
└──────────▲────────────────────────────────────────▲───────────────────────────────────────────────────────────────┘
           │ IDeviceScanner                         │ IVendorCatalogProvider / IDeviceNameLookup
┌──────────┴──────────────────┐        ┌────────────┴───────────────────────────────────────────────┐
│ DriverCompass.Scanner.Windows│        │ DriverCompass.Data                                          │
│ WMI (System.Management)      │        │ VendorCatalogValidator (JSON Schema + safety rules)         │
│ Win32_PnPEntity ⋈            │        │ VendorCatalogProvider: GitHub ─► cache ─► embedded           │
│ Win32_PnPSignedDriver, …     │        │ embedded vendors.json, vendors.schema.json, pci.ids          │
└──────────────────────────────┘        └─────────────────────────────────────────────────────────────┘

DriverCompass.Installer (stage 3): separate elevated process – not used yet.
```

## Key decisions

| Decision | Reason |
|---|---|
| Core has no Windows dependency | Logic is unit-testable on any machine; the mock scanner covers typical problem devices. |
| WMI behind `IWmiQuery` | The mapping from WMI rows to models is tested with fake rows. |
| Status rules for "generic" are data-driven (`genericDrivers`) | Many Microsoft inbox drivers (USB, HID, NVMe) are perfectly fine; only known cases such as `display.inf` or `hdaudio.inf` are flagged. |
| Every URL is validated twice | Once when the catalog is loaded (validator), again when links are built and opened (`UrlSafety`, `Launcher`). |
| Catalog downgrade protection | A remote or cached list older than the built-in one is ignored, so an old or replayed file cannot remove entries. |
| Texts are keys in Core, localized in the App | Core stays UI-free; reports get texts via `ITextProvider`. |
| Display name in `Directory.Build.props` | One place to rename; read at runtime via `AppInfo`. |
| Unpackaged self-contained EXE first | Runs without certificate or installation; MSIX is prepared for later. |

## Status logic

| Condition | Status |
|---|---|
| `ConfigManagerErrorCode` 28 | Driver missing |
| Code 22 | Turned off (information only) |
| Code 45 | Not connected (hidden) |
| Any other code ≠ 0 | Problem |
| Microsoft driver matching a `genericDrivers` rule | Only a generic driver |
| Everything else | OK (vendor driver, Windows standard driver, or no driver needed) |

ROOT\, SWD\, HTREE\, UMB\ and software classes (SoftwareDevice, Volume, AudioEndpoint …) are system devices. They are hidden by default.
