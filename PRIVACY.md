# Datenschutz / Privacy

## Deutsch

DriverCompass läuft vollständig lokal auf deinem PC.

- **Keine Telemetrie**, keine Analyse, keine Absturzberichte an Dritte.
- **Gerätedaten** wie Hardware-IDs, Treiberversionen, PC-Modell oder Seriennummer bleiben auf dem PC.
- **Einzige automatische Netzwerkverbindung (Standard):** Beim Start lädt die App die öffentliche Herstellerliste (`vendors.json`) von GitHub (`raw.githubusercontent.com`). Das ist ein einfacher Abruf einer Datei. Es werden keine Gerätedaten gesendet, nur die üblichen technischen Verbindungsdaten wie IP-Adresse und User-Agent `DriverCompass/<Version>`. Beim ersten Start zeigt die App vor dieser Verbindung einen Datenschutz-Hinweis, in dem du sie gleich abschalten kannst; später geht das in den Einstellungen.
- **Update-Hinweis (optional, standardmäßig aus):** Wenn du „Beim Start nach neuer DriverCompass-Version suchen“ einschaltest, fragt die App beim Start `api.github.com` nach der neuesten veröffentlichten Version dieses Projekts. Gesendet werden nur die üblichen Verbindungsdaten (IP-Adresse, User-Agent `DriverCompass/<Version>`), keine Gerätedaten. Gibt es eine neuere Version, zeigt die App einen Link zur Release-Seite. Es wird nichts automatisch heruntergeladen oder installiert.
- **Links** öffnen sich erst beim Klick in deinem Browser. Die Links enthalten aber Daten:
  - Hersteller-Links können **Modell** oder **Seriennummer** enthalten, damit sich die genaue Supportseite öffnet. Die Seriennummer lässt sich in den Einstellungen ausschließen.
  - Links zum Microsoft Update Catalog enthalten die **Hardware-ID**.

  Diese Daten erhält der jeweilige Hersteller bzw. Microsoft nur, wenn du klickst.
- **Versionsprüfung (optional, standardmäßig aus):** Erst wenn du eine Quelle in den Einstellungen einschaltest **und** auf „Versionen prüfen“ klickst, nimmt die App Kontakt auf. Pro Quelle:
  - **Windows Update:** Die Windows-Update-Schnittstelle übermittelt die Hardware-IDs deiner Geräte an Microsoft, genau wie Windows Update selbst.
  - **Dell:** Lädt den Index `downloads.dell.com/catalog/CatalogIndexPC.cab` (keine Gerätedaten) und danach den Katalog für dein Modell. Dell sieht dabei die Modellkennung (System-ID).
  - **Lenovo:** Lädt `download.lenovo.com/catalog/<Maschinentyp>_Win11.xml` und die zugehörigen Paketbeschreibungen. Lenovo sieht den Maschinentyp.
  - **HP:** Lädt `hpia.hpcloud.hp.com/ref/platformList.cab` (keine Gerätedaten) und die Referenzdatei für deine Plattform. HP sieht Plattform-ID und Windows-Version.

  Die Seriennummer wird dabei nie verwendet. Die Kataloge werden unter `%LOCALAPPDATA%\DriverCompass\cache\versions` zwischengespeichert. Die Downloads laufen nur über HTTPS, nur von diesen fest eingetragenen Adressen, mit Signatur- bzw. Prüfsummen-Kontrolle.
- **Treiberinstallation (optional):** Wenn du bei einem von Windows Update gefundenen Treiber auf „Installieren …“ klickst und bestätigst, lädt Windows Update genau dieses Paket von Microsoft und installiert es. Dabei gilt dasselbe wie bei Windows Update selbst. Protokoll und Ergebnis liegen lokal unter `%ProgramData%\DriverCompass`.
- **Treiber aus Dell- oder HP-Paketen (optional):** Bei „Installieren …“ für einen Treffer aus dem Dell- oder HP-Katalog lädt DriverCompass den Katalog und das Treiberpaket direkt von `downloads.dell.com` bzw. `ftp.hp.com`. Dell bzw. HP sieht dabei Modell bzw. Plattform und welches Paket geladen wird – keine Seriennummer.
- **Treiber aus Lenovo-Paketen (optional):** Wie bei Dell und HP, aber von `download.lenovo.com`. Lenovo sieht den Maschinentyp und welches Paket geladen wird – keine Seriennummer.
- **Logs** (`%LOCALAPPDATA%\DriverCompass\logs`, 14 Tage) enthalten Geräteanzahlen und Fehlermeldungen, aber keine Seriennummern.
- **Berichte** (HTML/CSV) entstehen nur, wenn du exportierst. Die Seriennummer ist standardmäßig nicht enthalten. Prüfe Berichte, bevor du sie weitergibst.
- **Microsoft-Store-Ausgabe:** Sie installiert keine Treiber und hat keinen Update-Hinweis (der Store aktualisiert die App). Die Punkte zur Treiberinstallation und zum Update-Hinweis gelten dort also nicht. Einstellungen, Zwischenspeicher und Logs leitet Windows in den privaten Ordner des App-Pakets unter `%LOCALAPPDATA%\Packages\` um; beim Deinstallieren werden sie entfernt.

## English

DriverCompass runs entirely on your PC.

- **No telemetry**, no analytics, no crash reports to third parties.
- **Device data** such as hardware IDs, driver versions, PC model or serial number stays on the PC.
- **Only automatic network connection (default):** at startup the app downloads the public vendor list (`vendors.json`) from GitHub (`raw.githubusercontent.com`). It is a plain file request with no device data; only the usual technical connection data is sent, such as the IP address and the user agent `DriverCompass/<version>`. On first start the app shows a privacy notice before this connection, where you can turn it off right away; later you can do so in the settings.
- **Update hint (optional, off by default):** if you turn on "Check for a new DriverCompass version at startup", the app asks `api.github.com` at startup for the latest published version of this project. Only the usual connection data is sent (IP address, user agent `DriverCompass/<version>`), no device data. If there is a newer version, the app shows a link to the release page. Nothing is downloaded or installed automatically.
- **Links** only open in your browser when you click them. The links do contain data:
  - Manufacturer links may contain the **model** or **serial number**, so the exact support page opens. The serial number can be excluded in the settings.
  - Microsoft Update Catalog links contain the **hardware ID**.

  The manufacturer or Microsoft only receives this data when you click.
- **Version check (optional, off by default):** the app only connects after you turn a source on in the settings **and** click "Check versions". Per source:
  - **Windows Update:** the Windows Update interface sends the hardware IDs of your devices to Microsoft, exactly like Windows Update itself.
  - **Dell:** downloads the index `downloads.dell.com/catalog/CatalogIndexPC.cab` (no device data), then the catalog for your model. Dell sees the model ID (system ID).
  - **Lenovo:** downloads `download.lenovo.com/catalog/<machine type>_Win11.xml` and the package descriptions it lists. Lenovo sees the machine type.
  - **HP:** downloads `hpia.hpcloud.hp.com/ref/platformList.cab` (no device data), then the reference file for your platform. HP sees the platform ID and Windows version.

  The serial number is never used. Catalogs are cached in `%LOCALAPPDATA%\DriverCompass\cache\versions`. Downloads use HTTPS only, only from these hard-coded addresses, with signature or checksum checks.
- **Driver installation (optional):** when you click "Install …" for a driver found on Windows Update and confirm, Windows Update downloads exactly this package from Microsoft and installs it – the same as Windows Update itself. Log and result stay local in `%ProgramData%\DriverCompass`.
- **Drivers from Dell or HP packages (optional):** when you click "Install …" for a match from the Dell or HP catalog, DriverCompass downloads the catalog and the driver package directly from `downloads.dell.com` or `ftp.hp.com`. Dell or HP sees the model or platform and which package is downloaded – no serial number.
- **Drivers from Lenovo packages (optional):** as for Dell and HP, but from `download.lenovo.com`. Lenovo sees the machine type and which package is downloaded – no serial number.
- **Logs** (`%LOCALAPPDATA%\DriverCompass\logs`, 14 days) contain device counts and error messages, but no serial numbers.
- **Reports** (HTML/CSV) are only created when you export them. The serial number is excluded by default. Review reports before sharing them.
- **Microsoft Store edition:** it installs no drivers and has no update hint (the Store updates the app), so the points about driver installation and the update hint do not apply there. Windows redirects settings, cache and logs into the app package's private folder below `%LOCALAPPDATA%\Packages\`; uninstalling removes them.
