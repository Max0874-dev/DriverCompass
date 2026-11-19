# DriverCompass

**Deutsch** · [English](README.en.md)

DriverCompass ist eine kostenlose Open-Source-App für Windows 10 und 11. Sie erkennt alle Geräte in deinem PC, zeigt dir, welche Treiber **fehlen**, **Fehler melden** oder **nur generisch** sind (z. B. „Microsoft Basic Display Adapter“), und führt dich zur **offiziellen Treiberseite** des Herstellers.

- 🔒 **Alles lokal.** Keine Telemetrie, keine Gerätedaten verlassen den PC. Automatisch lädt die App nur die öffentliche Herstellerliste von GitHub (abschaltbar). Die optionale Versionsprüfung fragt erst, wenn du sie einschaltest und startest. Auch der Hinweis auf neue DriverCompass-Versionen ist standardmäßig aus.
- 🚦 **Einfache Ansicht** mit Ampel, verständlichen Namen und einem Button „Treiber holen“.
- 🔬 **Expertenmodus** mit Hardware-IDs, INF-Datei, Treiberanbieter, Version, Datum, Fehlercode, Suche, Filter, Sortierung und Export (HTML/CSV).
- 🔄 **Versionsprüfung** (optional): „Versionen prüfen“ vergleicht installierte Treiber mit offiziellen Quellen: Windows Update sowie die Kataloge von Dell, Lenovo und HP. Ohne offizielle Quelle steht ehrlich „Version unbekannt – bitte Link prüfen“. Jede Quelle ist standardmäßig aus, siehe [PRIVACY.md](PRIVACY.md).
- 🛠️ **Installation** (Windows Update, Dell, HP, Lenovo): „Installieren …“ installiert genau einen bestätigten Treiber – bei Dell, HP und Lenovo nur den signierten Treiber aus dem offiziellen Paket (Dell/HP ohne Herstellerprogramme; Lenovo-Pakete entpackt Lenovos signiertes Programm einmal mit Adminrechten) –, mit Wiederherstellungspunkt, Protokoll und „Rückgängig machen“ im Verlauf. Adminrechte nur für diesen Schritt (UAC). **Ohne Gewähr** – Treiber stammen von Drittanbietern, sichere vorher wichtige Daten.
- 🌍 Deutsch und Englisch.
- 🛡️ Keine Adminrechte nötig – außer für eine bestätigte Treiberinstallation.

> **Status:** Stufe 1 (Erkennen und Links), Stufe 2 (Versionsvergleich) und Stufe 3 (Installation aus Windows Update sowie Dell-, HP- und Lenovo-Paketen) sind fertig, siehe [docs/roadmap.md](docs/roadmap.md).

## Wie die Links entstehen

Für jedes Gerät schlägt DriverCompass Quellen in dieser Reihenfolge vor:

1. **PC- oder Mainboard-Hersteller** (Dell/Alienware, HP, Lenovo, ASUS, MSI, GIGABYTE, ASRock, Acer, Surface, Fujitsu, Dynabook/Toshiba, MEDION, Samsung, Razer, Huawei, Framework, Panasonic). Wo möglich direkt mit Seriennummer oder Modell. Bei selbst gebauten PCs wird der Mainboard-Hersteller genutzt.
2. **Chip-Hersteller** (NVIDIA, AMD, Intel, Realtek, Logitech …), ermittelt aus der Hardware-ID (`PCI\VEN_10DE…` → NVIDIA).
3. **Microsoft Update Catalog** als Suche mit der Hardware-ID.

Links öffnen sich erst beim Klick in deinem Browser. Jede URL muss HTTPS sein und auf einer für den Hersteller hinterlegten Domain liegen.

## Voraussetzungen

| Zum Benutzen | Zum Entwickeln |
|---|---|
| Windows 10 1809+ oder Windows 11 (x64/ARM64) | [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) |
| | optional: Visual Studio 2026 mit dem Workload „WinUI-Anwendungsentwicklung“ |

Ein Windows SDK muss nicht separat installiert werden. Es kommt über das NuGet-Paket `Microsoft.Windows.SDK.BuildTools`.

## Bauen und starten

```powershell
winget install Microsoft.DotNet.SDK.10        # einmalig
dotnet build DriverCompass.slnx -p:Platform=x64
dotnet test --solution DriverCompass.slnx -p:Platform=x64
dotnet run --project src/DriverCompass.App -p:Platform=x64
```

**Demo ohne echte Problemgeräte:** Mit `--mock` zeigt die App typische Beispielgeräte, etwa eine Netzwerkkarte ohne Treiber, eine Grafikkarte mit Basic Display Adapter oder ein Bluetooth-Gerät mit Code 43:

```powershell
dotnet run --project src/DriverCompass.App -p:Platform=x64 -- --mock
```

**Release-Build als ZIP** (self-contained, ohne Installation lauffähig):

```powershell
dotnet publish src/DriverCompass.App -c Release -p:Platform=x64 -r win-x64 --self-contained -o publish/win-x64
```

## Projektstruktur

```
src/
  DriverCompass.Core/              Modelle, Interfaces, Parser, Status-Logik, Link-Resolver, Export, Mock-Scanner
  DriverCompass.Scanner.Windows/   WMI-Scanner (Win32_PnPEntity, Win32_PnPSignedDriver, …)
  DriverCompass.Data/              Herstellerliste: Validator, Laden (Online → Cache → eingebettet), pci.ids
  DriverCompass.VersionProviders/  Stufe 2: Windows Update, Dell-, Lenovo-, HP-Kataloge (HTTPS, Signatur, Prüfsumme)
  DriverCompass.Installer/         Stufe 3: Installer-Engine (läuft nur in „DriverCompass.exe --elevated“ per UAC)
  DriverCompass.App/               WinUI-3-App (MVVM, CommunityToolkit.Mvvm, Serilog)
tests/                             xUnit-Tests (Parser, Status, Resolver, Export, Validator, WMI-Mapping, Übersetzungen)
data/
  vendors.json                     Herstellerliste (versioniert)
  vendors.schema.json              JSON-Schema
  examples/                        Beispiel für einen neuen Eintrag
  third-party/pci.ids              PCI-Namensdatenbank (BSD-3-Clause)
docs/                              Architektur, Roadmap, Release-Checkliste, Hersteller eintragen
```

Den **Anzeigenamen** der App änderst du an genau einer Stelle: `<DriverCompassDisplayName>` in [Directory.Build.props](Directory.Build.props). Dort stehen auch die Repo-URL und die URL der Online-Herstellerliste (für Forks: auf den eigenen GitHub-Account anpassen).

## Neue Hersteller eintragen

Beiträge zur Herstellerliste sind sehr willkommen. Kurz gesagt:

1. In [data/vendors.json](data/vendors.json) einen Eintrag nach dem Muster in [data/examples/new-vendor.example.json](data/examples/new-vendor.example.json) ergänzen.
2. `dataVersion` erhöhen.
3. `dotnet test --project tests/DriverCompass.Data.Tests` ausführen. Derselbe Validator läuft auch in der App und in der CI.
4. Pull Request öffnen.

Details stehen in [CONTRIBUTING.md](CONTRIBUTING.md) und [docs/adding-vendors.md](docs/adding-vendors.md).

## Dateien auf deinem PC

| Was | Wo |
|---|---|
| Einstellungen | `%LOCALAPPDATA%\DriverCompass\settings.json` |
| Herstellerliste (Cache) | `%LOCALAPPDATA%\DriverCompass\cache\` |
| Logs (14 Tage) | `%LOCALAPPDATA%\DriverCompass\logs\` |
| Versionskataloge (Cache) | `%LOCALAPPDATA%\DriverCompass\cache\versions\` |
| Installationsprotokoll, Ergebnisse, Log des erhöhten Prozesses (nur für Admins schreibbar) | `%ProgramData%\DriverCompass\` |

## Lizenz und Datenschutz

[MIT](LICENSE). Drittanbieter: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Datenschutz: [PRIVACY.md](PRIVACY.md). Sicherheitslücken bitte nach [SECURITY.md](SECURITY.md) melden.

**Code signing policy:** Die Signatur der Windows-Releases über die SignPath Foundation wurde am 19.11.2026 beantragt; bis dahin sind die Releases unsigniert. Regeln und Zuständigkeiten: [docs/code-signing-policy.md](docs/code-signing-policy.md).

DriverCompass ist ein unabhängiges Projekt und steht in keiner Verbindung zu den genannten Herstellern. Alle Marken gehören ihren Inhabern.
