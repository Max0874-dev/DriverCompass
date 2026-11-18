# Architecture

```
            ┌────────────────────────── DriverCompass.App (WinUI 3, MVVM) ──────────────────────────┐
            │ MainWindow · SimpleView · ExpertView · SettingsDialog · ViewModels · Localizer (.resw) │
            └───────────────┬──────────────────────────────────────────────────────┬─────────────────┘
                            │ IDeviceAnalysisService                               │ IReportExporter
┌───────────────────────────▼──────────── DriverCompass.Core (net10.0, no Windows deps) ──────────────────────────┐
│ DeviceAnalysisService:  IDeviceScanner ─► IDeviceStatusEvaluator ─► ILinkResolver ─► sort (problems first)       │
│ HardwareIdParser · SystemDeviceFilter · SystemInfoSanitizer · VendorCatalog · UrlSafety · Html/Csv exporters     │
│ MockDeviceScanner · JsonSettingsService · VersionCheckService (stage 2) · stage-3 interfaces (installer, verifier)│
└──────────▲────────────────────────────────────────▲───────────────────────────────────────────────────────────────┘
           │ IDeviceScanner                         │ IVendorCatalogProvider / IDeviceNameLookup
┌──────────┴──────────────────┐        ┌────────────┴───────────────────────────────────────────────┐
│ DriverCompass.Scanner.Windows│        │ DriverCompass.Data                                          │
│ WMI (System.Management)      │        │ VendorCatalogValidator (JSON Schema + safety rules)         │
│ Win32_PnPEntity ⋈            │        │ VendorCatalogProvider: GitHub ─► cache ─► embedded           │
│ Win32_PnPSignedDriver, …     │        │ embedded vendors.json, vendors.schema.json, pci.ids          │
└──────────────────────────────┘        └─────────────────────────────────────────────────────────────┘

DriverCompass.VersionProviders (stage 2): IVersionProvider plugins – Windows Update (WUA COM), Dell, Lenovo, HP catalogs.
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

## Stage 2 – version check

- `IVersionCheckService` (Core) runs only the sources the user switched on (`AppSettings.EnabledVersionSources`, empty by default) and only when "Check versions" is clicked.
- Each source is an `IVersionProvider` and answers for **all devices at once** (one catalog download or one Windows Update search per run). Results are merged per device: newer version (highest wins) → nothing newer → unknown. A failing source is reported in the status bar; the others still run.
- Matching: catalog hardware IDs against the device's hardware IDs plus vendor-specific compatible IDs (`PCI\VEN_x&DEV_y`). Class-only IDs such as `PCI\CC_0200` are never used.
- Comparison: INF driver versions as four numbers (`DriverVersion`); the date only if no version is comparable. Windows Update reports no version number, so an offered driver counts as "newer" (Windows Update only offers better-ranked drivers).

| Source | Data | Trust |
|---|---|---|
| Windows Update | WUA COM `IsInstalled=0 and Type='Driver'`, works without admin rights | Windows' own channel |
| Dell | `downloads.dell.com/catalog/CatalogIndexPC.cab` → model CAB (systemID = `SystemSKUNumber`) | Authenticode (Dell) on both CABs, SHA-256 of the model CAB from the index |
| Lenovo | `download.lenovo.com/catalog/<MT>_Win11.xml` → package descriptors (`DetectInstall/_Driver`) | HTTPS + SHA-256 of every descriptor from the catalog |
| HP | `hpia.hpcloud.hp.com/ref/platformList.cab` → `ref/<id>/<id>_64_<10.0/11.0>.<release>.cab` (platform ID = `Win32_BaseBoard.Product`) | Authenticode (HP Inc.) on both CABs |

Safety rules in `DriverCompass.VersionProviders`:

- `CatalogDownloader`: HTTPS only, exact host allow-list hard-coded per provider (not in `vendors.json`), redirects checked hop by hop, size limits, cache with ETag/24 h.
- `ExpandCabExtractor`: checks the Authenticode signature and signer **before** extracting with `%SystemRoot%\System32\expand.exe` into a fresh temp folder; exactly one file is accepted, nothing is executed.
- `SafeXml`: no DTDs, no external resolution, size cap, streaming reads.
- Links shown to the user (Dell driver page, HP release notes) are checked with `UrlSafety` against `dell.com` / `hp.com`.
