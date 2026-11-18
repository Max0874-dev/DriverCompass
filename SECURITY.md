# Security Policy

DriverCompass deals with drivers, so security reports are taken seriously.

## Reporting a vulnerability

Please **do not** open a public issue. Use GitHub's private reporting instead: *Security → Report a vulnerability* in this repository. Expect a first response within 7 days.

Especially relevant:

- ways to make the app open or download from non-official or non-HTTPS URLs
- a manipulated `vendors.json` that passes validation
- privilege escalation through the elevated installer process (`DriverCompass.exe --elevated`)
- leaks of device data or serial numbers off the PC

## Design principles

- The main app runs **without administrator rights** (`asInvoker`).
- The vendor list is validated before use: schema, HTTPS only, domain allow-list, size limit, downgrade protection.
- Links are re-checked before opening and are only opened after a user click.
- Stage 3: installation runs in a **separate, UAC-elevated process** (`DriverCompass.exe --elevated --request <file>`), one driver per confirmation.

## Threat model of the elevated process

- **Input is untrusted.** The request file in the user's profile carries only IDs (request ID, device instance ID, Windows Update UpdateID, or a log entry ID). It is size-limited, strictly parsed (unknown fields are rejected) and validated; instance IDs containing quotes are refused.
- **Everything is re-checked elevated:** the device is read again via WMI, Windows Update is searched again for the UpdateID, and the update must be of type *Driver* with a `DriverHardwareID` that belongs to this very device. License acceptance is never done silently.
- **Restore point first.** If Windows does not create a new one (System Restore off, 24-hour limit), nothing is installed unless the user explicitly allowed it in a second confirmation.
- **Package integrity:** Windows Update downloads and verifies the Microsoft-signed package itself.
- **No writes into user-writable locations.** Results and the install log go to `%ProgramData%DriverCompass`, created with an explicit ACL (Administrators/SYSTEM full control, Users read). An existing folder that is a link or not owned by Administrators/SYSTEM is refused.
- **Rollback is bound to the log:** only `oemNN.inf` packages recorded in the protected log are removed, and only if their SHA-256 still matches; `pnputil` is called by absolute path with fixed arguments.
- OEM packages (Dell/Lenovo/HP .exe) are not installed yet; their pages only open in the browser.
