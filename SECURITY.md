# Security Policy

DriverCompass deals with drivers, so security reports are taken seriously.

## Reporting a vulnerability

Please **do not** open a public issue. Use GitHub's private reporting instead: *Security → Report a vulnerability* in this repository. Expect a first response within 7 days.

Especially relevant:

- ways to make the app open or download from non-official or non-HTTPS URLs
- a manipulated `vendors.json` that passes validation
- privilege escalation once the stage-3 installer exists
- leaks of device data or serial numbers off the PC

## Design principles

- The main app runs **without administrator rights** (`asInvoker`).
- The vendor list is validated before use: schema, HTTPS only, domain allow-list, size limit, downgrade protection.
- Links are re-checked before opening and are only opened after a user click.
- Stage 3 (planned): installation in a separate elevated process that re-verifies HTTPS origin, domain, SHA-256 and the driver signature itself, one driver at a time, only after explicit confirmation.
