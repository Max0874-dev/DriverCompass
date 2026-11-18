# Contributing / Mitmachen

Thanks for helping! / Danke fürs Mitmachen! Contributions in **English or German** are welcome.

## Ground rules

- **Privacy first:** no telemetry, no analytics, no new network calls that send device data.
- **Official sources only:** driver links must point to the manufacturer's own HTTPS site, never to mirrors or driver-download portals.
- **No admin rights** in the main app. Anything that needs elevation belongs in `DriverCompass.Installer` (stage 3).
- New UI texts go into **both** `src/DriverCompass.App/Strings/de-DE/Resources.resw` and `…/en-US/Resources.resw`. A test checks this.

## Development

```powershell
dotnet build DriverCompass.slnx -p:Platform=x64
dotnet test --solution DriverCompass.slnx -p:Platform=x64
dotnet run --project src/DriverCompass.App -p:Platform=x64 -- --mock   # demo data
```

Logic belongs in `DriverCompass.Core` (UI-free, unit-testable). Use the `MockDeviceScanner` to test without problem hardware.

## Adding a manufacturer (most common contribution)

Full guide: [docs/adding-vendors.md](docs/adding-vendors.md). In short:

1. Find the vendor ID:
   - Device Manager → device → *Details* → *Hardware IDs*.
   - Or in DriverCompass expert mode.
   - `PCI\VEN_10EC&…` means PCI vendor `10EC`. `USB\VID_046D&…` means USB vendor `046D`.
2. Copy the matching block from [data/examples/new-vendor.example.json](data/examples/new-vendor.example.json) into `data/vendors.json`, under `chipVendors` (chip makers) or `oems` (PC/mainboard makers).
3. Rules – the validator enforces all of them:
   - IDs are upper-case hex (`10EC`, not `10ec`); one vendor ID belongs to exactly one entry.
   - Every URL starts with `https://` and its host is inside `allowedDomains` (subdomains allowed).
   - OEM templates may use only `{serial}`, `{model}`, `{sku}`, `{board}`, `{family}`, never in the host name, and `requires` lists exactly the placeholders used.
   - `match` patterns are .NET regular expressions (case-insensitive, max. 200 chars).
4. Raise `dataVersion` (format `YYYY.M.D.N`, e.g. `2026.11.20.1`).
5. Run `dotnet test --project tests/DriverCompass.Data.Tests` and open a pull request. CI runs the same check.

Once merged into `main`, apps load the new list automatically at their next start.

## Pull requests

- One topic per PR, with a short description of what changed and why.
- `dotnet test` must pass.
- By contributing you agree that your contribution is licensed under the MIT license.
