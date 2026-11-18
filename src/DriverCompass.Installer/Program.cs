using DriverCompass.Core;

// Stage 3 placeholder. Planned flow (see docs/roadmap.md):
//  1. Read an InstallRequest (one driver) from the unelevated app.
//  2. Re-verify EVERYTHING itself: HTTPS origin, allow-listed domain, SHA-256, Authenticode/catalog signature.
//  3. Create a restore point (or report clearly why not).
//  4. pnputil /add-driver <inf> /install, write the install log, return the result.
Console.Error.WriteLine($"{AppInfo.DisplayName} Installer: driver installation is planned for stage 3 and not implemented yet.");
return 1;
