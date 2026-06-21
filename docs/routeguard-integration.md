# RouteGuard Integration (Phase 5)

MasselGUARD owns UX, profiles, Wi-Fi automation, tunnel orchestration, and config SSOT. RouteGuard owns WFP split-tunnel enforcement, route table manipulation, and the routing decision engine.

## Architecture

```
MasselGUARD UI → Tauri → MasselGUARDAgent → RouteGuardBridgeService
                                              ↓ JSON-RPC
                                    \\.\pipe\RouteGuard → routeguard-service
```

Tunnel connect/disconnect remains in MasselGUARD `TunnelService`. The bridge pushes **tunnel context** (name, adapter, endpoint) so RouteGuard compiles policy against the live adapter without calling `tunnel.connect`.

## Availability states

| State | Detection | Behavior |
|-------|-----------|----------|
| **absent** | No install marker and pipe unreachable | Config-only; bridge no-op |
| **installed** | `routeguard-service.exe` found; pipe fails | Offer `routeguard.start` |
| **running** | Pipe + `service.ping` OK | Full sync and event relay |

## MasselGUARD RPC

| Method | Description |
|--------|-------------|
| `routeguard.status` | Availability, remote capabilities, sync metadata |
| `routeguard.capabilities` | Negotiated feature matrix |
| `routeguard.sync` | `{ force? }` — reconcile split tunnel rules |
| `routeguard.routing.test` | Proxy to RouteGuard `routing.test` |
| `routeguard.start` | Launch installed service elevated |

Aliases: `split_tunnel.get/set` unchanged; `set` triggers sync when bridge enabled.

## RouteGuard RPC (bridge client)

| Method | Purpose |
|--------|---------|
| `service.ping` | Liveness + version |
| `service.capabilities` | Feature flags |
| `routing.import_rules` | Bulk replace from MasselGUARD projection |
| `routing.set_tunnel_context` | Active tunnel adapter context |
| `events.poll` | `{ sinceId, limit }` — event relay source |
| `domain.status` | Domain cache stats and `effective` flag |
| `routing.test` | Flow decision simulation |

## Events (EventEnvelope v1)

| MasselGUARD type | When |
|------------------|------|
| `routeguard.availability_changed` | absent ↔ installed ↔ running |
| `routeguard.sync_completed` | After reconcile |
| `routeguard.routing_changed` | RouteGuard `routing.reloaded` relay |
| `routeguard.domain_resolved` | RouteGuard `routing.dns.resolved` |
| `routeguard.domain_route_added` | Host route installed |
| `routeguard.domain_route_expired` | TTL expiry purge |
| `routeguard.domain_recovered` | Startup cache reload |
| `routeguard.network_lock_changed` | WFP delegation enable/disable |

Tunnel `tunnel.*` events remain MasselGUARD-authoritative.

## Network Lock coexistence

- **Default:** Windows Firewall via `FirewallEnforcer`
- **Opt-in:** `networkLockWfpDelegation` in config → RouteGuard WFP via `RouteGuardEnforcer`; mutually exclusive with FirewallEnforcer

## Graceful fallback

- `useRouteGuardBridge=false` → no RouteGuard calls; Phase 4 behavior preserved
- RouteGuard crash → availability drops to `installed`; MasselGUARD tunnels stay up
- Sync failure → last error in `routeguard.status`; config still saved locally

## Manual test matrix

1. Bridge off → save rules → no pipe traffic
2. Bridge on, RG stopped → status `installed` → Start → sync
3. Connect kill-switch tunnel → tunnel context pushed → WFP filters applied
4. Edit app rule → save → `routeguard.sync_completed` event
5. Enable WFP delegation → Network Lock uses RouteGuard backend only
6. Legacy WPF app unaffected
7. Domain rules → `domain.status.resolvedIps` increases after DNS through proxy

## Domain routing (Phase 6 + 6.5)

See `RouteGuard/docs/DOMAIN_ROUTING.md` for full architecture.

- `service.capabilities.features.domainRoutingEffective` drives UI badge
- `service.capabilities.features.calloutDriver` — `routeguard-callout.sys` device present
- `routeguard.status.domain` exposes `{ rules, resolvedIps, effective, kernelRedirect, driverPresent, redirectStats? }`
- Configure RouteGuard `[routing.domain_dns]` for proxy listen/upstream/`redirect_port_53`/`kernel_redirect`
- Install `drivers/routeguard-callout/routeguard-callout.inf` for transparent UDP/TCP :53 redirect
- AWG profiles: place `tunnel.dll` in RouteGuard install dir; `service.capabilities.features.awg` reports availability
- `negotiated.awg` reflects RouteGuard AWG backend probe

## Out of scope

- MasselGUARD tunnels via RouteGuard `tunnel.connect` (MasselGUARD owns connect; RouteGuard provides AWG backend when used directly)
- SCM service registration (RouteGuard installer)
- Phantun transport (capability flags only)
