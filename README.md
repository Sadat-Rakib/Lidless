# Lidless

Windows tray app: keep your PC awake with the lid closed (for coding agents).

Lidless sits in the notification area and, when you turn it on, stops Windows from sleeping so builds, downloads, and coding agents keep running after you close the lid.

> Open source under the [MIT License](LICENSE). Original keep-awake idea and macOS implementation © 2026 Nghia Luong. Windows port © 2026 Sadat-Rakib.

## Install

Requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). The CI artifact is framework-dependent (not self-contained).

1. Download the latest `Lidless-windows-x64` artifact from [GitHub Actions](https://github.com/Sadat-Rakib/Lidless/actions) (or build locally).
2. Copy `Lidless.exe` (and the DLLs next to it) somewhere durable, e.g. `%LOCALAPPDATA%\Lidless`.
3. Run `Lidless.exe`. The laptop icon appears in the notification area (check the `^` overflow next to the clock).
4. Click the icon → **Keep awake with lid closed**.

No installer and no admin prompt for a typical home PC. You can optionally enable **Launch at login** from the tray menu.

## Build

Requires the .NET 8 SDK on Windows 10 1809+ / Windows 11.

```bat
dotnet test Lidless.sln --configuration Release
dotnet publish src\Lidless.App\Lidless.App.csproj -c Release -r win-x64 --self-contained false -o artifacts\Lidless
```

CI runs the same commands on `windows-latest`.

This app is **WPF** (unpackaged) rather than WinUI 3: the tray icon is a Win32 `NotifyIcon`, and WPF builds cleanly on GitHub Actions without a Windows App SDK workload.

## How it works

Closing a laptop lid is **not** the same as idle sleep.

| Mechanism | What it does | What it does not do |
| --- | --- | --- |
| `SetThreadExecutionState(ES_SYSTEM_REQUIRED \| ES_AWAYMODE_REQUIRED)` | Stops idle / away-mode sleep | Does not change lid-close behavior |
| `PowerCreateRequest` + `PowerSetRequest(SystemRequired)` | Holds a system power request (more reliable on Modern Standby) | Does not change lid-close behavior |
| Current scheme lid action → **Do nothing** (AC and DC) | The setting Windows uses for “When I close the lid” | Can be overridden by OEM utilities, Group Policy, or firmware |

Lidless uses all three. While keep-awake is on it snapshots the current lid-close actions, sets them to **Do nothing**, and holds the power request. Turning it off, quitting, or a watchdog timeout restores the snapshot.

The watchdog is the same `Lidless.exe` started as `--watchdog`. It shares a heartbeat in a named memory-mapped file. If the tray process exits or goes quiet for 90 seconds while restore is armed, the watchdog writes the original lid actions back through `powercfg`.

## Safety

- **Pause when running hot** — ACPI thermal zones at or above 90 °C (when WMI exposes them).
- **Only while charging** — auto-pause on battery.
- **Low-battery cutoff** — default 20% off AC; set to Never to disable.
- **Automatically enable when charging** — arms keep-awake whenever you are on AC and the other guards pass.
- **Auto-off timer** — 15 minutes to 4 hours, or no limit. Choosing a duration also turns keep-awake on.
- **Launch at login** — `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Keep the PC plugged in and ventilated under heavy use. A closed chassis plus a long agent run will get hot.

## Windows realities (please read)

Software cannot promise “lid closed = running” on every laptop.

- **OEM overlays** (Lenovo Vantage, Dell Power Manager, HP, Surface) may reset lid actions or sleep in firmware.
- **Group Policy / MDM** can lock the power scheme; Lidless will report that `powercfg` failed.
- **Some machines sleep on lid close no matter what Windows says.** Try it once with a ping or a long job before you rely on it.
- The **internal display usually turns off** when the lid is closed. That is hardware. The PC should keep running.
- **Lid actions persist across reboot.** macOS `SleepDisabled` is cleared by a reboot; Windows “Do nothing” is not. Lidless writes a session file under `%AppData%\Lidless` and restores it on the next launch if a crash left it behind. If Lidless and the watchdog both die (hard power loss) before that, change **Settings → System → Power → Lid** yourself, or open Lidless once.
- Lidless does **not** treat a user who already chose “Do nothing” as keep-awake-on. External *disable* (lid action no longer Do nothing while Lidless thought it was on) is detected.

## Architecture

- **`Lidless.Core`** — policy, timer, watchdog decision, settings. Classes with static helpers; no Windows APIs. Unit-tested on any OS.
- **`Lidless.App`** — WPF tray UI, `powercfg` lid policy, power requests, heartbeat + watchdog process.

## Security

See [SECURITY.md](SECURITY.md). Report vulnerabilities privately via GitHub Security Advisories.

## License

[MIT](LICENSE) © 2026 Nghia Luong. Windows port © 2026 Sadat-Rakib.
