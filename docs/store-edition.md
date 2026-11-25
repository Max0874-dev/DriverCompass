# Microsoft Store edition

**Status:** published in the Microsoft Store on 2026-11-22 (version 0.3.1.0, x64 and ARM64): <https://apps.microsoft.com/detail/9PKVXM3Q7P7L> (Store ID `9PKVXM3Q7P7L`).

DriverCompass is released in two editions:

| | GitHub edition (ZIP) | Microsoft Store edition (MSIX) |
|---|---|---|
| Detect & link (stage 1) | ✅ | ✅ |
| Compare versions (stage 2) | ✅ | ✅ |
| Install a driver (stage 3), install history, undo | ✅ | – |
| Update hint (GitHub Releases API, opt-in) | ✅ | – (the Store updates the app) |
| Signature | not yet signed, SmartScreen warns | signed by Microsoft |

## Why the Store edition installs no drivers

- Microsoft does not accept apps into the Store that need elevation for any part of their functionality ([Prepare to package a desktop application](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare)). Stage 3 runs `DriverCompass.exe --elevated` via UAC.
- The `allowElevation` capability exists, but Microsoft does not recommend it for Store apps and in most cases does not approve it.
- The Store policies (version 7.20, 10.1.5) exclude non-Microsoft drivers from what a Store app may let users acquire.

The Store edition therefore does not just hide stage 3. It is built without it:

- `-p:DriverCompassEdition=Store` defines `STORE_EDITION`.
- The `DriverCompass.Installer` project is not referenced.
- `Program.cs` has no `--elevated` entry point.
- `App.xaml.cs` registers `NoInstallation` and `NoAppUpdateChecker` (in `src/DriverCompass.Core/Installation/NoInstallation.cs`).

What the Store edition offers instead: the **"How to"** guide per device leads to the driver step by step. A newer Windows Update driver is installed with Windows' own *Optional updates* page (DriverCompass opens it); for Dell, HP and Lenovo packages the guide opens the official package or model page and names the package, version and the maker's own update program. DriverCompass itself downloads no driver and starts no installer.

The workflow *Store package* checks that the package contains no installer and does not ask for `allowElevation`. The Store edition does not mention the GitHub edition, because Store products may only offer other products that are themselves in the Store.

## Build locally

```powershell
dotnet publish src/DriverCompass.App/DriverCompass.App.csproj -c Release -p:Platform=x64 -r win-x64 `
  -p:DriverCompassEdition=Store -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false `
  -p:UapAppxPackageBuildMode=StoreUpload -p:AppxBundle=Never -p:AppxSymbolPackageEnabled=false `
  -p:AppxPackageDir="$PWD\store\"
```

Symbol packages stay off: the MSIX tooling fails to create them under the dotnet CLI (`System.Security.Permissions` not found), and the Store does not need them.

To run the unsigned package on your own PC, turn on Windows *Developer Mode*. Then unzip the `.msix` into a folder and register it with `Add-AppxPackage -Register <folder>\AppxManifest.xml`. Afterwards remove it with `Get-AppxPackage *DriverCompass* | Remove-AppxPackage`.

Settings, cache and logs: the app writes to `%LOCALAPPDATA%\DriverCompass` as usual. In the Store edition Windows redirects these writes into the package's private storage, which other programs do not see; uninstalling the app removes it. Reads still see existing files, so on a PC that already used the GitHub edition the Store edition starts with those settings (and does not show the first-start privacy notice again).

Tested on 2026-11-21 with the registered x64 package (Developer Mode): the app starts, scans the real hardware, shows no "Install …", no history and no update hint, and the Windows Update version check works inside the package ("Windows Update: checked").

## Submitting to the Store (done by the maintainer)

1. **Create a developer account** in [Partner Center](https://learn.microsoft.com/en-us/windows/apps/get-started/sign-up). Choose *Individual developer*. Registration is free when you use the new sign-up flow ("Get started for free" on the Store developer page); Microsoft verifies your identity.
2. **Reserve the name** "DriverCompass" (Apps and games → New product → MSIX or PWA app).
3. **Copy the package identity** from *Product management → Product identity* into three repository secrets (GitHub → Settings → Secrets and variables → Actions → Secrets). The values are not secret, but for an individual account they contain your real name; secrets are masked in the public workflow logs, variables are not:
   - `STORE_IDENTITY_NAME` = `Package/Identity/Name`
   - `STORE_PUBLISHER` = `Package/Identity/Publisher`
   - `STORE_PUBLISHER_DISPLAY_NAME` = `Package/Properties/PublisherDisplayName`
4. **Build the packages.** Run the workflow *Store package* by hand, or push a `v*` tag. Download the artifacts `store-package-x64` and `store-package-ARM64`; each contains one `.msixupload`.
5. **Create the submission:**
   - *Packages:* upload both `.msixupload` files. Below them, under *Device family availability*, tick **Windows 10/11 Desktop**. Without it Partner Center warns that the product "will not be available to customers on Windows 10/11": the Store page opens by its link, but the app does not show up in the Store search.
   - *Store listing:* description and screenshots. `docs/images/simple-view-*.png` and `expert-view-*.png` are a start; the Store asks for at least 1366×768.
   - *Privacy policy URL:* `https://github.com/Max0874-dev/DriverCompass/blob/main/PRIVACY.md`.
   - *Age ratings:* the IARC questionnaire.
   - *Pricing:* free.
6. **Certification notes.** A suggestion:

   > DriverCompass lists the devices on this PC whose driver is missing, failing or only generic, and opens the official driver page of the PC, mainboard or chip manufacturer in the default browser. It does not download or install drivers and never asks for administrator rights. runFullTrust is needed for the WMI device query (Win32_PnPEntity, read-only) and the optional Windows Update driver search (Windows Update Agent API, search only). The only automatic network access is the download of a public vendor list from raw.githubusercontent.com, which the user can turn off on first start. Source code: https://github.com/Max0874-dev/DriverCompass

The first submission (2026-11-21) passed certification as described: self-contained Windows App SDK in the package, `runFullTrust` with the justification above, category *Utilities & tools*, age rating 3+ / USK 0. It was set to publish only after "Publish now", which the maintainer clicked on 2026-11-22. Afterwards the Store page opened by its link, but the search did not find the app, and *Device family availability* had no device family ticked. Since 2026-11-23 the app shows up in the Store search.

## Updating the Store edition

1. Release the new version as usual (raise `<Version>`, tag `vX.Y.Z`). The tag also starts *Store package*; its artifacts contain the `.msixupload` files with the Store identity from the secrets.
2. In Partner Center open DriverCompass → *Update* (a new submission copies the previous one).
3. *Packages:* upload the two new `.msixupload` files and remove the old ones. The version must be higher than the published one. Check that **Windows 10/11 Desktop** is still ticked under *Device family availability*.
4. Partner Center asks again why the app needs `runFullTrust`. Paste the current justification (below) and keep it in step with what the app does.
5. Adjust the store listings if something visible changed (for example *What's new in this version*), then *Submit to the Store*.

The Store keeps installed copies up to date by itself; there is nothing to change in the app.

`runFullTrust` justification used for 0.4.0:

> DriverCompass is a desktop app (WinUI 3, packaged Win32) that lists the devices on this PC whose driver is missing, failing or only generic, and opens the official driver page of the PC, mainboard or chip manufacturer in the default browser. runFullTrust is needed because it is a full-trust desktop app and uses Win32 APIs that are not available to AppContainer apps: the read-only WMI device query (Win32_PnPEntity, Win32_ComputerSystem, Win32_BaseBoard, Win32_BIOS) and the optional Windows Update driver search (Windows Update Agent COM API, search only). The optional version check also extracts the vendors' signed catalog files (Dell, HP) with Windows' own expand.exe. The app does not download or install drivers and never asks for administrator rights; for a newer Windows Update driver it only opens Settings > Windows Update > Optional updates. Network access: a public vendor list from raw.githubusercontent.com (can be turned off on first start), opt-in vendor catalogs (Dell, Lenovo, HP), and HP's public platform list when the user opens an HP support link. Source code: https://github.com/Max0874-dev/DriverCompass

Submissions: 0.3.1.0 (first, published 2026-11-22), 0.4.0.0 (submitted 2026-11-25).

Tested on 2026-11-23 with the app installed from the Store (0.3.1.0, x64, signed by the Store): the Dell and HP catalogs only run on PCs of those makers, and the test PC is not one. So the risky part was run inside the Store package with `Invoke-CommandInDesktopPackage`, the same way `CatalogDownloader` and `CabExtractor` do it: download `CatalogIndexPC.cab` (Dell) and `platformList.cab` (HP) over HTTPS into `%LOCALAPPDATA%\DriverCompass\cache`, check the Authenticode signer (both valid: Dell Technologies Inc., HP Inc.) and extract with `System32\expand.exe` into `%TEMP%\DriverCompass`. Both returned exit code 0 and exactly one file. Lenovo's catalog is a plain XML download over HTTPS without a CAB, the same way the vendor list is loaded, which works in the package.

## Not verified yet

- **Privacy notice in a fresh install:** the test PC already had settings from the GitHub edition, so the first-start notice of the Store edition was not shown. Check it on a PC without DriverCompass data.
- **Dell, Lenovo and HP version check end to end** on a PC of that maker with the Store edition (the download, signature check and extraction were tested inside the package, see above).
