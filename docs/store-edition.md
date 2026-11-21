# Microsoft Store edition

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

Settings, cache and logs: the app writes to `%LOCALAPPDATA%\DriverCompass` as usual. In the Store edition Windows redirects these writes into the package's private folder below `%LOCALAPPDATA%\Packages\`. Uninstalling the app removes them.

## Submitting to the Store (done by the maintainer)

1. **Create a developer account** in [Partner Center](https://learn.microsoft.com/en-us/windows/apps/get-started/sign-up). Choose *Individual developer*. Registration is free when you use the new sign-up flow ("Get started for free" on the Store developer page); Microsoft verifies your identity.
2. **Reserve the name** "DriverCompass" (Apps and games → New product → MSIX or PWA app).
3. **Copy the package identity** from *Product management → Product identity* into three repository variables (GitHub → Settings → Secrets and variables → Actions → Variables):
   - `STORE_IDENTITY_NAME` = `Package/Identity/Name`
   - `STORE_PUBLISHER` = `Package/Identity/Publisher`
   - `STORE_PUBLISHER_DISPLAY_NAME` = `Package/Properties/PublisherDisplayName`
4. **Build the packages.** Run the workflow *Store package* by hand, or push a `v*` tag. Download the artifacts `store-package-x64` and `store-package-ARM64`; each contains one `.msixupload`.
5. **Create the submission:**
   - *Packages:* upload both `.msixupload` files.
   - *Store listing:* description and screenshots. `docs/images/simple-view-*.png` and `expert-view-*.png` are a start; the Store asks for at least 1366×768.
   - *Privacy policy URL:* `https://github.com/Max0874-dev/DriverCompass/blob/main/PRIVACY.md`.
   - *Age ratings:* the IARC questionnaire.
   - *Pricing:* free.
6. **Certification notes.** A suggestion:

   > DriverCompass lists the devices on this PC whose driver is missing, failing or only generic, and opens the official driver page of the PC, mainboard or chip manufacturer in the default browser. It does not download or install drivers and never asks for administrator rights. runFullTrust is needed for the WMI device query (Win32_PnPEntity, read-only) and the optional Windows Update driver search (Windows Update Agent API, search only). The only automatic network access is the download of a public vendor list from raw.githubusercontent.com, which the user can turn off on first start. Source code: https://github.com/Max0874-dev/DriverCompass

## Not verified yet

- **Windows Update search:** whether the Windows Update driver search (stage 2) works inside the MSIX package. Test this before the first submission.
- **Package format:** whether certification accepts the self-contained Windows App SDK in the package, or asks for the framework package instead.
- **Review:** how reviewers judge an app that shows driver problems and links to manufacturer pages.
