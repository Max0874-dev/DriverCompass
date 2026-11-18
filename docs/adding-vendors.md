# Adding vendors to `data/vendors.json`

The vendor list maps hardware vendor IDs to manufacturers and their official driver pages. The app downloads the newest version from the `main` branch at startup, validates it, and falls back to its cached or built-in copy if anything is wrong.

## Structure

| Section | Purpose |
|---|---|
| `chipVendors` | Chip manufacturers, found via the vendor ID in the hardware ID |
| `oems` | PC / mainboard manufacturers, found via `Win32_ComputerSystem.Manufacturer`, or `Win32_BaseBoard.Manufacturer` for self-built PCs |
| `genericDrivers` | Microsoft inbox drivers that count as "only generic" (e.g. `display.inf`) |

### Chip vendor

```json
{
  "id": "realtek",                      // lower-case, unique
  "name": "Realtek",                    // shown in the UI
  "ids": {
    "pci":  ["10EC"],                   // PCI-SIG vendor IDs (also used for HD-Audio codecs)
    "usb":  ["0BDA"],                   // USB-IF vendor IDs
    "acpi": ["RTL"],                    // ACPI/PNP prefixes, 3–4 characters
    "pnp":  []                          // monitor manufacturer IDs (3 letters)
  },
  "allowedDomains": ["realtek.com"],    // every URL must be on one of these (or a subdomain)
  "driverPages": [
    { "url": "https://www.realtek.com/audio", "deviceClasses": ["MEDIA"] },  // only for this setup class
    { "url": "https://www.realtek.com/Download/Overview" }                   // for all devices
  ],
  "note": { "en": "Optional hint", "de": "Optionaler Hinweis" }
}
```

Some products use **another company's USB vendor ID** – e.g. Lian Li hubs report ENE's `0CF2`. Mapping `0CF2` to Lian Li would mislabel every other ENE device, so such products are listed one by one as `VID:PID` (upper-case hex). They are checked before the vendor IDs and may not be listed twice:

```json
"ids": { "usbProducts": ["0CF2:A100", "0416:7373"] }
```

Device classes are the Windows setup class names: `Display`, `Net`, `MEDIA`, `Bluetooth`, `USB`, `HIDClass`, `SCSIAdapter`, `System`, `Camera` … You can see them in the expert view.

### OEM (PC / mainboard manufacturer)

```json
{
  "id": "dell",
  "name": "Dell",
  "match": ["^Dell"],                   // regex on the manufacturer string from SMBIOS
  "allowedDomains": ["dell.com"],
  "supportUrls": [                      // most specific first – the first one whose values exist is used
    { "template": "https://www.dell.com/support/home/product-support/servicetag/{serial}/drivers", "requires": ["serial"] },
    { "template": "https://www.dell.com/support/home/drivers", "requires": [] }
  ]
}
```

Placeholders:

| Placeholder | Value |
|---|---|
| `{serial}` | BIOS serial number / service tag. Only used if the user allows it. |
| `{model}` | System model, or the board name for self-built PCs |
| `{sku}` | System SKU |
| `{family}` | System family |
| `{board}` | Mainboard product name |

Values are URL-encoded. Placeholders such as "To Be Filled By O.E.M." count as missing, and then the next template is tried.

### Generic driver rule

```json
{ "id": "generic-bluetooth", "infName": "bth.inf", "deviceClass": "Bluetooth", "enumerators": ["USB", "PCI"], "category": "bluetooth" }
```

A device gets the status "only a generic driver" when all of these hold:

- the driver provider matches `providerPattern` (default `^Microsoft`)
- the INF name or device name (`deviceNamePattern`) matches
- the optional `deviceClass` matches
- the optional `enumerators` contain the start of the instance ID (`USB\…`, `PCI\…`)

Keep rules narrow, because many Windows inbox drivers are perfectly fine.

## Checklist before the pull request

- [ ] Official manufacturer site, HTTPS, opened in a browser and checked
- [ ] IDs upper-case hex, not already assigned to another entry
- [ ] `dataVersion` raised
- [ ] `dotnet test --project tests/DriverCompass.Data.Tests` passes

## What the validator rejects

Anything that is not valid against `vendors.schema.json`. In addition:

- `http://` URLs, URLs outside `allowedDomains`, non-default ports, user info in URLs
- placeholders in the host name, unknown placeholders, `requires` not matching the template
- invalid regular expressions
- duplicate IDs, or a vendor ID assigned twice
- files larger than 2 MB, a `schemaVersion` newer than the app supports, or a `dataVersion` older than the built-in copy
