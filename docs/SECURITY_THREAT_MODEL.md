# MasselGUARD / RouteGuard — STRIDE threat model (Phase 13)

## Trust boundaries

```
User/admin → Tauri UI → \\.\pipe\MasselGUARD → MasselGUARDAgent (LocalSystem)
                                              → \\.\pipe\RouteGuard → RouteGuard (LocalSystem)
                                              → Kernel driver (callout.sys)
Agent → releases.masselguard.net (HTTPS)
Agent → telemetry.masselguard.net (opt-in HTTPS)
```

## Assets

| Asset | Sensitivity |
|-------|-------------|
| WireGuard private keys / PSK | Critical |
| Tunnel endpoints | High |
| Split-tunnel app paths | Medium |
| Support bundle contents | Medium–High (tier-dependent) |
| Update manifest + binaries | Critical |
| Kernel driver | Critical |

## STRIDE analysis

### IPC (`\\.\pipe\MasselGUARD`, `\\.\pipe\RouteGuard`)

| Threat | Mitigation | Residual |
|--------|------------|----------|
| **Spoofing** | Named pipe SDDL; strict mode for stable | Beta: any Authenticated User can call RPC |
| **Tampering** | No method-level auth | Local malware with user token |
| **Repudiation** | Event seq + NDJSON log | No audit log for RPC |
| **Info disclosure** | Event schema excludes secrets | RPC can export tunnel configs |
| **DoS** | Event replay rate limit | No global RPC rate limit |
| **EoP** | Services run as LocalSystem | `update.apply` = SYSTEM file write |

### Updater (`UnifiedUpdateService`)

| Threat | Mitigation | Residual |
|--------|------------|----------|
| **Tampering** | Ed25519 manifest sig + SHA256 per file; backup rollback | Pubkey must be configured (Phase 13) |
| **Tampering** | Legacy UpdateChecker zip | Deprecate for stable |
| **DoS** | Staging dir size | Large manifest download |

### Installer / driver

| Threat | Mitigation | Residual |
|--------|------------|----------|
| **Tampering** | EV signing + attestation | Admin can install other drivers |
| **Tampering** | WiX rollback CA | Driver CA failure mid-install |
| **EoP** | Deferred elevated custom actions | Standard Windows model |

### Support bundle

| Threat | Mitigation | Residual |
|--------|------------|----------|
| **Info disclosure** | 3-tier redaction; consent | `full` tier + env gate |
| **Info disclosure** | Size caps | User may attach wrong tier |

### Telemetry (Phase 13)

| Threat | Mitigation | Residual |
|--------|------------|----------|
| **Info disclosure** | Allowlist metrics; forbidden key lint | Future schema drift |
| **Linkability** | Random installId | Same ID across sessions by design |

## Priority mitigations (Phase 13)

1. Wire Ed25519 manifest public key; fail-closed on beta/stable when signature missing
2. Fuzz parsers (WG, AWG, LWO, IPC, redaction)
3. Telemetry allowlist + privacy review
4. Document beta pipe ACL risk; enforce strict for stable
5. RPC rate limit on `update.apply` (future)

## References

- [event-security.md](event-security.md)
- [network-lock-security.md](network-lock-security.md)
- [API_STABILITY.md](API_STABILITY.md)
- [TELEMETRY_PRIVACY.md](TELEMETRY_PRIVACY.md)
- [checklists/SECURITY_CHECKLIST.md](checklists/SECURITY_CHECKLIST.md)
