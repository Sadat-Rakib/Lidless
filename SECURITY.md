# Security Policy

## Supported versions

Security fixes land on the latest `main`. Please test against current `main` before reporting.

## Reporting a vulnerability

**Do not open a public issue for security problems.**

Report privately via GitHub **Security Advisories** on this repository (Security tab → Report a vulnerability).

Include steps to reproduce, the Windows version, and the impact you observed.

## Security-relevant surface

Lidless is unpackaged and not sandboxed. It does **not** install a SYSTEM service or require admin on a typical PC. These areas still matter:

- **Lid-close policy** (`powercfg` on the current user’s power scheme). A stuck “Do nothing” value means the PC may stay awake in a bag.
- **Power requests** (`SetThreadExecutionState`, `PowerCreateRequest`) that block idle sleep while keep-awake is on.
- **Watchdog process** (`Lidless.exe --watchdog`) plus `%AppData%\Lidless\session.json`. If the tray app dies, the watchdog must restore the previous lid actions. A reboot does **not** reset those actions by itself.
- **Launch-at-login** writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Issues in restore-on-crash, unauthorized keep-awake, or the watchdog failing to put sleep back are the most valuable to report.
