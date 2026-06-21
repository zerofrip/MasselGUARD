# MasselGUARD / RouteGuard — Release Engineering

Phase 11 release process for public beta distribution.

## Version management

Single source of truth: [`version.json`](../version.json)

```powershell
# Bump all project files
.\scripts\bump-version.ps1 -MasselGuardVersion 3.7.0 -Codename "New Name" -RouteGuardVersion 0.2.0 -Channel beta
```

Sync only (after manual version.json edit):

```powershell
.\scripts\sync-version.ps1
```

`BUILD.bat` reads version from `version.json` via `read-version.ps1`.

## Build profiles

See [`release/profiles.json`](../release/profiles.json) for signing, driver, update channel, and telemetry matrix.

| Profile | Use |
|---------|-----|
| **developer** | Local `BUILD.bat` / `cargo build` |
| **nightly** | CI artifacts, internal VMs |
| **beta** | Public beta (EV-signed, attestation driver) |
| **stable** | Production release |

## Installer (WiX 4 + Burn)

```
installer/
  Product.wxs       # MSI product + features
  Components.wxs    # Files + Windows services
  Driver.wxs        # Callout driver + custom actions
  Bundle.wxs        # Burn bootstrapper (.NET 10 + MSI)
  build-installer.ps1
  custom-actions/   # pnputil install/uninstall/rollback
```

Build on Windows (WiX 4 CLI required):

```powershell
.\BUILD.bat
cargo build --release   # RouteGuard, from sibling repo
.\installer\build-installer.ps1 -Channel beta
```

### Features

- **Core** — GUI, Agent service, RouteGuard service
- **AWG** — `tunnel.dll`
- **Phantun** — `phantun_client.exe`
- **DomainRedirect** — `routeguard-callout.sys` (reboot may be required)

## Driver deployment

1. WDK build → `drivers/routeguard-callout/`
2. EV sign `.sys` + catalog (see RouteGuard `SIGNING.md`)
3. Microsoft Partner Center attestation (beta/stable)
4. Installer runs `pnputil /add-driver` via deferred custom action
5. OEM inventory tracked in `HKLM\SOFTWARE\RouteGuard\Driver`

Dev VMs: `bcdedit /set testsigning on` + test certificate.

## Updates

Manifest-driven unified updater: [`MasselGUARDAgent/Release/UnifiedUpdateService.cs`](../MasselGUARDAgent/Release/UnifiedUpdateService.cs)

- Channel URL: `https://releases.masselguard.net/{channel}/manifest.json`
- Agent RPC: `update.check`, `update.apply`
- Components: GUI, Agent, RouteGuard, optional driver (reboot)
- Rollback: backup in `%ProgramData%\MasselGUARD\updates\backup\`

Generate manifest after build:

```powershell
.\scripts\generate-release-manifest.ps1 -Channel beta -DistDir dist -RouteGuardDir ..\RouteGuard\target\release
```

## Crash reporting

- Local: `%ProgramData%\MasselGUARD\crashes\*.json`
- Opt-in: `config.json` → `crashReportingEnabled`
- Included in diagnostics export bundle

## Security checklist (beta gate)

- [ ] Pipe SDDL (`MASSELGUARD_STRICT_PIPE=1` for stable)
- [ ] `ROUTE_GUARD_VERIFY_SIGNATURES=1` on beta/stable builds
- [ ] Diagnostics `full` tier requires `ROUTE_GUARD_FULL_DIAGNOSTICS=1`
- [ ] EV signing on all PE/DLL/SYS
- [ ] Manifest ed25519 signature verification

## CI

GitHub Actions: [`.github/workflows/release.yml`](../.github/workflows/release.yml)

Secrets required for signed builds:
- `EV_CERT_PFX` + `EV_CERT_PASSWORD`
- `MANIFEST_SIGNING_KEY` (ed25519 private, base64)

## Migration from zip updates

Legacy `UpdateChecker` GitHub zip flow remains as fallback. New installs use MSI + manifest updater. WPF `DoInstall` deprecated in favor of WiX installer.
