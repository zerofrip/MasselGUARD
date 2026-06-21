# API Stability Policy — MasselGUARD Public Beta

This document defines stability expectations for JSON surfaces exposed to the Tauri UI, CLI consumers, and support tooling during the public beta period.

## Schema inventory

| Surface | Version field | Location | Stability class |
|---------|---------------|----------|-----------------|
| MasselGUARD events NDJSON | `version: 1` | [event-schema-v1.md](event-schema-v1.md) | **Stable** — additive only |
| Agent JSON-RPC | implicit | `MasselGUARDAgent/Ipc/IpcModels.cs` | **Stable** — method names frozen for beta |
| Bridge projection | `bridge.schemaVersion: 1` | `RouteGuardBridgeService.cs` | **Stable** |
| RouteGuard IPC (bridge subset) | `service.capabilities.schemaVersion: 3` | RouteGuard `handler.rs` | **Versioned** — bump on breaking |
| Observability snapshot | `schemaVersion: 1` | RouteGuard observability | **Versioned** — additive |
| Update manifest | `schemaVersion: 1` | `UnifiedUpdateService.cs` | **Versioned** |
| Support bundle manifest | `schemaVersion: 1` | `SupportBundleBuilder.cs` | **Versioned** |

## Versioning strategy

- **Product SemVer:** MasselGUARD `MAJOR.MINOR.PATCH`; RouteGuard independent `0.x` until 1.0.
- **Schema integer:** `{schema}Version` increments only on **breaking** JSON shape changes.
- **Additive changes** (new optional fields, new event types, new RPC methods): no schema bump; document in changelog.
- **Bridge contract:** MasselGUARD depends only on the bridge subset of RouteGuard IPC listed in [routeguard-integration.md](routeguard-integration.md). Direct `routeguard-cli` / full IPC is **unstable** for external consumers.

## Compatibility policy (public beta)

| Change type | Agent RPC | Events v1 | RG bridge IPC |
|-------------|-----------|-----------|---------------|
| New optional response field | Allowed | Allowed | Allowed |
| New event type | Allowed | Allowed | N/A |
| New RPC method | Allowed | N/A | Allowed |
| Rename/remove field | Forbidden without major + deprecation | Forbidden | Requires capabilities bump |
| Behavior change | Changelog | Changelog | Negotiated via `service.capabilities` |

## Mixed-version tolerance

Agent N may talk to RouteGuard N-1 during upgrade if `service.capabilities` advertises required features. Bridge degrades gracefully (`availability: installed`).

## Deprecation policy

1. Mark deprecated in docs + optional `agent.status.deprecated` array.
2. Emit `agent.notification` on first use per session.
3. Minimum **2 minor releases** or **90 days** (whichever is longer) before removal.
4. v0 events: read-only; removal target = MasselGUARD 4.0.

## Support bundle / diagnostics tiers

| Tier | Use |
|------|-----|
| `sanitized` | Default public beta; safe for unsolicited ticket attachment |
| `support` | User-initiated ticket; may include paths and endpoints |
| `full` | Admin elevation + `ROUTE_GUARD_FULL_DIAGNOSTICS=1`; never auto-upload |

## Stable Agent RPC methods (beta freeze)

Core tunnel, config, WiFi, history, network lock, RouteGuard bridge, update, and support export:

- `support.export`, `support.export.status`
- `routeguard.diagnostics.export` (internal; prefer `support.export` in UI)
- `update.check`, `update.apply`

New methods may be added; existing method names and required params will not be renamed or removed during beta without deprecation.
