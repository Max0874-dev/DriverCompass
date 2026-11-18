# DriverCompass

[Deutsch](README.md) · **English**

DriverCompass is a free, open-source app for Windows 10 and 11. It detects every device in your PC, shows which drivers are **missing**, **reporting errors** or **only generic** (e.g. "Microsoft Basic Display Adapter"), and takes you to the manufacturer's **official driver page**.

- 🔒 **Everything stays local.** No telemetry, no device data leaves the PC. The only automatic download is the public vendor list from GitHub (can be turned off). The optional version check only connects once you turn it on and start it.
- 🚦 **Simple view** with a traffic light, plain names and one "Get driver" button.
- 🔬 **Expert mode** with hardware IDs, INF file, driver provider, version, date, error code, search, filters, sorting and export (HTML/CSV).
- 🔄 **Version check** (optional): "Check versions" compares installed drivers with official sources: Windows Update plus the Dell, Lenovo and HP catalogs. Without an official source it honestly says "Version unknown – please check the link". Every source is off by default, see [PRIVACY.md](PRIVACY.md).
- 🛠️ **Installation** (Windows Update, Dell, HP, Lenovo): "Install …" installs exactly one confirmed driver – for Dell, HP and Lenovo only the signed driver from the official package (Dell/HP without running vendor programs; Lenovo packages are extracted once by Lenovo's signed program with admin rights) –, with restore point, log and "Undo" in the history. Admin rights only for this step (UAC). **No warranty** – drivers come from third parties, back up important data first.
- 🌍 English and German.
- 🛡️ No administrator rights needed – except for a confirmed driver installation.

> **Status:** stage 1 (detect and link), stage 2 (version comparison) and stage 3 (installing from Windows Update and from Dell, HP and Lenovo packages) are done, see [docs/roadmap.md](docs/roadmap.md).

## How links are chosen

For every device DriverCompass suggests sources in this order:

1. **PC or mainboard manufacturer** (Dell/Alienware, HP, Lenovo, ASUS, MSI, GIGABYTE, ASRock, Acer, Surface, Fujitsu, Dynabook/Toshiba, MEDION, Samsung, Razer, Huawei, Framework, Panasonic), using the serial number or model where possible. Self-built PCs use the mainboard vendor.
2. **Chip vendor** (NVIDIA, AMD, Intel, Realtek, Logitech …), derived from the hardware ID (`PCI\VEN_10DE…` → NVIDIA).
3. **Microsoft Update Catalog** search for the hardware ID.

Links only open in your browser when you click them. Every URL must be HTTPS and on a domain registered for that vendor.

## Requirements

| To use | To develop |
|---|---|
| Windows 10 1809+ or Windows 11 (x64/ARM64) | [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) |
| | optional: Visual Studio 2026 with the "WinUI application development" workload |

No separate Windows SDK install is needed; it comes via the `Microsoft.Windows.SDK.BuildTools` NuGet package.

## Build & run

```powershell
winget install Microsoft.DotNet.SDK.10        # once
dotnet build DriverCompass.slnx -p:Platform=x64
dotnet test --solution DriverCompass.slnx -p:Platform=x64
dotnet run --project src/DriverCompass.App -p:Platform=x64
```

**Demo without real problem devices:** `--mock` shows typical example devices (network card without driver, GPU on the basic display adapter, Bluetooth with code 43 …):

```powershell
dotnet run --project src/DriverCompass.App -p:Platform=x64 -- --mock
```

**Release build as ZIP** (self-contained, runs without installation):

```powershell
dotnet publish src/DriverCompass.App -c Release -p:Platform=x64 -r win-x64 --self-contained -o publish/win-x64
```

## Project layout

```
src/
  DriverCompass.Core/              models, interfaces, parser, status logic, link resolver, export, mock scanner
  DriverCompass.Scanner.Windows/   WMI scanner (Win32_PnPEntity, Win32_PnPSignedDriver, …)
  DriverCompass.Data/              vendor list: validator, loading (online → cache → embedded), pci.ids
  DriverCompass.VersionProviders/  stage 2: Windows Update, Dell, Lenovo, HP catalogs (HTTPS, signature, checksum)
  DriverCompass.Installer/         stage 3: installer engine (runs only in "DriverCompass.exe --elevated" via UAC)
  DriverCompass.App/               WinUI 3 app (MVVM, CommunityToolkit.Mvvm, Serilog)
tests/                             xUnit tests
data/                              vendors.json, JSON schema, example entry, pci.ids
docs/                              architecture, roadmap, release checklist, adding vendors
```

Change the app's **display name** in exactly one place: `<DriverCompassDisplayName>` in [Directory.Build.props](Directory.Build.props). The repository URL and the URL of the online vendor list are defined there too (forks: point them to your own GitHub account).

## Adding vendors

Contributions to the vendor list are very welcome:

1. Add an entry to [data/vendors.json](data/vendors.json), following [data/examples/new-vendor.example.json](data/examples/new-vendor.example.json).
2. Raise `dataVersion`.
3. Run `dotnet test --project tests/DriverCompass.Data.Tests`. The same validator runs in the app and in CI.
4. Open a pull request.

See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/adding-vendors.md](docs/adding-vendors.md).

## Files on your PC

| What | Where |
|---|---|
| Settings | `%LOCALAPPDATA%\DriverCompass\settings.json` |
| Vendor list cache | `%LOCALAPPDATA%\DriverCompass\cache\` |
| Logs (14 days) | `%LOCALAPPDATA%\DriverCompass\logs\` |
| Version catalogs (cache) | `%LOCALAPPDATA%\DriverCompass\cache\versions\` |
| Install log, results, log of the elevated process (writable by admins only) | `%ProgramData%\DriverCompass\` |

## License & privacy

[MIT](LICENSE). Third-party components: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Privacy: [PRIVACY.md](PRIVACY.md). Please report vulnerabilities as described in [SECURITY.md](SECURITY.md).

DriverCompass is an independent project and not affiliated with any of the manufacturers mentioned. All trademarks belong to their owners.
