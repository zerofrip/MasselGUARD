# Support playbook (beta)

## Exporting a bundle

1. Open **Diagnostics** → **Export support bundle**.
2. First export shows consent dialog; optional crash reports are **local only** unless crash upload is enabled in Settings → Privacy.
3. Default tier is **sanitized** (beta). Dev builds can select **support** tier.

## Reading `diagnostics.json`

| Field | Meaning |
|-------|---------|
| `healthScore` | 0–100 composite from RouteGuard observability |
| `masselguardVersion` | Installed MasselGUARD version |
| `routeguardStatus.availability` | `running`, `installed`, `missing` |
| `flags` | Redaction notes applied to this bundle |

**Health interpretation:**

- **90–100:** Healthy — tunnel, transport, routing nominal.
- **70–89:** Degraded — check transport recovery attempts, handshake age, driver presence.
- **Below 70:** Critical — tunnel down, network lock violations, or driver missing when kernel redirect expected.

## Transport recovery

1. Open `observability.json` → `transport.recovery.attempts`.
2. If `lastFailureReason` is set, correlate with `logs/routeguard-tail.txt`.
3. Common fixes: restart RouteGuard service, verify endpoint reachability (support tier only).

## Updater rollback

1. Open `updater-status.json`.
2. `backupRoot` contains versioned tree from last `update.apply`.
3. Stop MasselGUARDAgent + RouteGuard services, restore files from backup, restart services.
4. If manifest fetch failed, `manifestAvailable: false` — check channel URL and signature config.

## Known false positives

| Symptom | Often benign when |
|---------|-------------------|
| Driver absent | Domain redirect / kernel DNS not enabled |
| RouteGuard `installed` not `running` | User stopped service; MasselGUARD can start via bridge |
| Low health with no tunnel | No active tunnel — expected in idle state |

## Escalation

Request **support** tier re-export when:

- Sanitized bundle lacks endpoint/path data needed to diagnose routing or split-tunnel rules.
- User consents to sharing app paths and peer endpoints in ticket.

Never request **full** tier via unsolicited upload. Full tier requires admin elevation and `ROUTE_GUARD_FULL_DIAGNOSTICS=1`.

## Bundle layout reference

```
support_bundle-{id}-{ts}.zip
├── manifest.json
├── diagnostics.json
├── agent-status.json
├── routeguard-status.json
├── observability.json
├── event-history.json
├── updater-status.json
├── routeguard-bundle.zip   (support/full, size permitting)
├── crash-reports/
└── logs/
    ├── agent-tail.txt
    ├── routeguard-tail.txt
    └── tunnel-history.json (full tier + opt-in)
```
