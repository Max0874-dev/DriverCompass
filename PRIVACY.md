# Datenschutz / Privacy

## Deutsch

DriverCompass läuft vollständig lokal auf deinem PC.

- **Keine Telemetrie**, keine Analyse, keine Absturzberichte an Dritte.
- **Gerätedaten** wie Hardware-IDs, Treiberversionen, PC-Modell oder Seriennummer bleiben auf dem PC.
- **Einzige automatische Netzwerkverbindung:** Beim Start lädt die App die öffentliche Herstellerliste (`vendors.json`) von GitHub (`raw.githubusercontent.com`). Das ist ein einfacher Abruf einer Datei. Es werden keine Gerätedaten gesendet, nur die üblichen technischen Verbindungsdaten wie IP-Adresse und User-Agent `DriverCompass/<Version>`. Du kannst das in den Einstellungen abschalten.
- **Links** öffnen sich erst beim Klick in deinem Browser. Die Links enthalten aber Daten:
  - Hersteller-Links können **Modell** oder **Seriennummer** enthalten, damit sich die genaue Supportseite öffnet. Die Seriennummer lässt sich in den Einstellungen ausschließen.
  - Links zum Microsoft Update Catalog enthalten die **Hardware-ID**.

  Diese Daten erhält der jeweilige Hersteller bzw. Microsoft nur, wenn du klickst.
- **Logs** (`%LOCALAPPDATA%\DriverCompass\logs`, 14 Tage) enthalten Geräteanzahlen und Fehlermeldungen, aber keine Seriennummern.
- **Berichte** (HTML/CSV) entstehen nur, wenn du exportierst. Die Seriennummer ist standardmäßig nicht enthalten. Prüfe Berichte, bevor du sie weitergibst.

## English

DriverCompass runs entirely on your PC.

- **No telemetry**, no analytics, no crash reports to third parties.
- **Device data** such as hardware IDs, driver versions, PC model or serial number stays on the PC.
- **Only automatic network connection:** at startup the app downloads the public vendor list (`vendors.json`) from GitHub (`raw.githubusercontent.com`). It is a plain file request with no device data; only the usual technical connection data is sent, such as the IP address and the user agent `DriverCompass/<version>`. You can turn this off in the settings.
- **Links** only open in your browser when you click them. The links do contain data:
  - Manufacturer links may contain the **model** or **serial number**, so the exact support page opens. The serial number can be excluded in the settings.
  - Microsoft Update Catalog links contain the **hardware ID**.

  The manufacturer or Microsoft only receives this data when you click.
- **Logs** (`%LOCALAPPDATA%\DriverCompass\logs`, 14 days) contain device counts and error messages, but no serial numbers.
- **Reports** (HTML/CSV) are only created when you export them. The serial number is excluded by default. Review reports before sharing them.
