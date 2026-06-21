# MasselGUARD WiX Installer

Production installer using **WiX Toolset 4** + **Burn** bootstrapper.

## Prerequisites

- WiX 4 CLI (`wix` on PATH)
- Windows build host
- Completed `BUILD.bat` output in `dist/`
- RouteGuard release binaries in `RouteGuard/target/release/`

## Build

```powershell
.\installer\build-installer.ps1 -Channel beta
```

Outputs in `dist/installer/`:
- `MasselGUARD-{version}-x64.msi`
- `MasselGUARD-{version}-beta-x64-setup.exe`

## Custom actions

| Script | When |
|--------|------|
| `Backup-Install.ps1` | Pre-install upgrade backup |
| `Install-Driver.ps1` | `pnputil /add-driver` for callout |
| `Uninstall-Driver.ps1` | Remove tracked OEM driver |
| `Rollback-Install.ps1` | MSI rollback restore |

## Services registered

| Service | Binary |
|---------|--------|
| `MasselGUARDAgent` | `MasselGUARDAgent.exe` |
| `RouteGuard` | `RouteGuard\routeguard-service.exe` |
