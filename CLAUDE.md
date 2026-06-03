# MasselGUARD — Codebase Guide

WireGuard tunnel manager for Windows. Two executables:
- **`MasselGUARD.exe`** — WPF GUI application (`OutputType=WinExe`, no console flash)
- **`MasselGUARDcli.exe`** — Console CLI (`OutputType=Exe`, PowerShell/cmd wait for exit)

.NET 10.

## Build

```bat
BUILD.bat          # requires .NET 10 SDK; output → dist\
```

Produces both `dist\MasselGUARD.exe` and `dist\MasselGUARDcli.exe`.

Current version: **3.5.0 — Hypersonic Quokka**
When bumping version, update **both** `UpdateChecker.cs` (`CurrentVersion` + `_codenames`) **and** `BUILD.bat` (`VERSION` + `CODENAME`).

## Key design decisions

- **Two exe split** — `MasselGUARD.exe` is `WinExe` so Windows never allocates a console (no flash). `MasselGUARDcli.exe` is `Exe` so terminals wait for it. Source is shared via `<Compile Include>` links from `MasselGUARDcli/MasselGUARDcli.csproj`.
- **`GenerateAssemblyInfo=false` + `GenerateTargetFrameworkAttribute=false`** on `MasselGUARD.csproj` — suppresses duplicate attributes caused by WPF's wpftmp compile step when `OutputType=WinExe`. Version info is still embedded via the wpftmp project's own generated attributes using `-p:` overrides from BUILD.bat.
- **`requireAdministrator` manifest** — UAC always elevates. Non-admin terminals get an isolated console (new window). `IsIsolatedConsole()` via `GetConsoleProcessList` detects this and pauses before exit.
- **`TearDownAdapter`** — after `EnsureStopped`, calls `WireGuardOpenAdapter` + `WireGuardCloseAdapter` to release any lingering kernel adapter. Needed because the WireGuardTunnelService exits but the adapter can outlive it.
- **`IsRunning()`** — checks `ServiceController.Status == Running` first (primary), then the WireGuard pipe as fallback.
- **`RefreshStatus()` resets DNS badge** — when poll detects tunnel going inactive externally (CLI disconnect), resets `_dnsStatus` and fires PropertyChanged for all DNS properties.
- **`KillSwitchService.Disable()`** — early-returns if tunnel not in `_active` HashSet, preventing spurious `[KillSwitch] Disabled` log entries.
- **`UpdateChecker.UpdateAsync`** — takes an `onShutdown` callback so WPF-specific `Application.Current.Dispatcher.Invoke(ShutdownApp)` stays in the GUI call site, keeping `UpdateChecker.cs` WPF-free.

## Project structure

```
MasselGUARD.csproj          # WinExe GUI project
Program.cs                  # Entry point: /service dispatch, then GUI
MasselGUARDcli/
  MasselGUARDcli.csproj     # Exe CLI project (links shared source via <Compile Include>)
  Program.cs                # CLI entry point → CliRunner.Run()
Cli/
  CliRunner.cs              # All CLI commands
  CliOutput.cs              # Console output helpers (Info/Ok/Error/PrintJson)
  WireGuardConf.cs          # WireGuard .conf parser/builder
Models/
  AppConfig.cs              # Main config model (serialised to config.json)
  StoredTunnel.cs           # Tunnel definition
  ConnectionHistoryEntry.cs
Services/
  TunnelService.cs          # Connect/Disconnect orchestration
  TunnelDll.cs              # P/Invoke to tunnel.dll + wireguard.dll  (root, not Services/)
  ConfigService.cs          # Load/Save %APPDATA%\MasselGUARD\config.json
  HistoryService.cs         # Load/Save %APPDATA%\MasselGUARD\history.json
  KillSwitchService.cs
  LogService.cs
  ScriptService.cs
ViewModels/
  MainViewModel.cs
  TunnelEntryViewModel.cs   # RefreshStatus() drives 1-second poll updates
Views/
  SettingsWindow.xaml(.cs)
  MainWindow.xaml(.cs)
UpdateChecker.cs            # GitHub release check + auto-update (WPF-free)
BUILD.bat
```

## CLI commands

| Command | Notes |
|---|---|
| `list` / `--list` | Supports `--group`, `--active` |
| `status` / `--status` | Active tunnel count |
| `connect <name>` | |
| `connect --default` | Uses `cfg.DefaultTunnel` |
| `connect --all` | Supports `--group` |
| `disconnect <name>` | |
| `disconnect-all` | Supports `--group`; exits 2 when nothing active |
| `info <name>` | Reads HistoryService for uptime/source |
| `log [n]` | Reads `history.json`; `--logtype extended` adds source column |
| `check-update` | Async GitHub call; exits 1 when update available |
| `version` / `--version` / `-v` | Shows codename, build stamp, update status |
| `help` / `--help` / `-h` | |

Global flags: `--json`, `--quiet`/`-q`, `--group <name>`, `--active`, `--logtype normal|extended`

Exit codes: `0` success · `1` error · `2` already in desired state

## Data files

| File | Location |
|---|---|
| Config | `%APPDATA%\MasselGUARD\config.json` |
| History | `%APPDATA%\MasselGUARD\history.json` |
| Tunnel configs | `<exedir>\tunnels\*.conf.dpapi` (DPAPI encrypted) |
| Themes | `<exedir>\theme\<folder>\theme.json` |
| Languages | `<exedir>\lang\*.json` |

## Tunnel sources

- `"local"` — managed by `TunnelDll` (wireguard-NT via `tunnel.dll`)
- anything else — WireGuard for Windows companion (`WireGuardTunnel$<name>` service)
