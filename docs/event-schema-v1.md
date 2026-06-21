# MasselGUARD Event Schema v1

## Envelope

Every event on `\\.\pipe\MasselGUARDAgent-events` is one NDJSON line.

### v1 (canonical)

```json
{
  "version": 1,
  "seq": 12345,
  "type": "tunnel.state_changed",
  "ts": "2026-06-20T15:30:00.1234567Z",
  "payload": {}
}
```

| Field | Type | Description |
|-------|------|-------------|
| `version` | int | Schema version (`1`) |
| `seq` | uint64 | Monotonic sequence; persisted across agent restarts |
| `type` | string | Dot-namespaced event type |
| `ts` | ISO-8601 UTC | Event timestamp |
| `payload` | object | Type-specific body |

### v0 (legacy, read-only)

```json
{
  "type": "tunnel.state_changed",
  "payload": {},
  "ts": 1718889600123
}
```

Parsers treat missing `version` as `0` and missing `seq` as unknown.

## Subscribe handshake

Client sends (first line on pipe):

```json
{ "op": "subscribe", "version": 1, "sinceSeq": 9990, "filters": ["tunnel.*", "wifi.*"] }
```

Agent responds:

```json
{ "op": "subscribed", "version": 1, "snapshotSeq": 10000, "replayFrom": 9991, "replayCount": 9 }
```

Then: `agent.snapshot` event, replay lines, live stream.

Filter update anytime:

```json
{ "op": "subscribe", "version": 1, "filters": ["*"] }
```

## Event catalog

### agent.*

| Type | Payload |
|------|---------|
| `agent.heartbeat` | `{ uptimeSecs }` |
| `agent.snapshot` | Full state + optional `meta: { seq, eventCount, ringCapacity }` |
| `agent.protocol_error` | `{ reason }` |

### tunnel.*

| Type | Payload |
|------|---------|
| `tunnel.state_changed` | `{ name, state, source }` |
| `tunnel.stats_updated` | `{ name, rxBytes, txBytes, rxRate, txRate, adapterUp? }` |
| `tunnel.handshake_updated` | `{ name, peerCount, lastHandshakeSecsAgo }` |
| `tunnel.created` | Tunnel summary object (name, profileSource, favorite, tags, …) |
| `tunnel.updated` | Tunnel summary object |
| `tunnel.deleted` | `{ name, profileSource }` |
| `tunnel.imported` | Tunnel summary object |
| `tunnel.cloned` | Tunnel summary object (also `{ summary, sourceName }` on clone RPC response path; event payload is summary) |

Reserved aliases (not emitted by default): `tunnel.connected`, `tunnel.disconnected`, `tunnel.stats`

### wifi.*

| Type | Payload |
|------|---------|
| `wifi.ssid_changed` | `{ ssid, isOpen }` |
| `wifi.rule_applied` | `{ action, tunnel, reason }` |

### network.*

| Type | Payload |
|------|---------|
| `network.changed` | `{ available, changeKind }` |

### system.*

| Type | Payload |
|------|---------|
| `log.entry` | `{ time, level, message }` |
| `notification` | `{ category, primary, secondary, durationMs }` |
| `connection.failed` | `{ tunnel, reason }` |

### networklock.*

| Type | Payload |
|------|---------|
| `networklock.enabled` | `{ mode, tunnelCount }` |
| `networklock.disabled` | `{}` |
| `networklock.policy_changed` | `{ mode, lanAccessEnabled, dnsPolicy }` |
| `networklock.recovered` | `{ mode, tunnelCount, reason }` |

### killswitch.*

| Type | Payload |
|------|---------|
| `killswitch.changed` | `{ mode, activeTunnels, networkLock, enforcementActive }` |

### routeguard.*

| Type | Payload |
|------|---------|
| `routeguard.availability_changed` | `{ availability, previous }` |
| `routeguard.sync_completed` | `{ ok, rulesApplied?, errors? }` |
| `routeguard.routing_changed` | `{ reason, ruleCount?, policyHash?, domainRules?, domainRoutes? }` |
| `routeguard.domain_resolved` | `{ domain, ips, ttlSecs, pattern, target }` |
| `routeguard.domain_route_added` | `{ ip, domain, target, expiresAt }` |
| `routeguard.domain_route_expired` | `{ ip, domain }` |
| `routeguard.domain_recovered` | `{ count, generation }` |
| `routeguard.network_lock_changed` | `{ active, backend?, tunnelCount? }` |
| `routeguard.awg_connected` | reserved |
| `routeguard.phantun_connected` | reserved |

### Reserved (future)

- `splittunnel.rule_added`, `splittunnel.rule_removed`
- `awg.connected`, `awg.disconnected`

## RPC

| Method | Description |
|--------|-------------|
| `agent.subscribe_info` | Returns `{ eventsPipe, schemaVersion }` |
| `agent.status` | Agent + event metrics + `networkLock` diagnostics slice |
| `agent.event_replay` | `{ sinceSeq, limit? }` → `{ events[], latestSeq }` |
| `agent.request_snapshot` | Current snapshot object |
| `routeguard.status` | Bridge + remote capabilities |
| `routeguard.capabilities` | Negotiated matrix |
| `routeguard.sync` | `{ force? }` reconcile |
| `routeguard.routing.test` | Flow simulation |
| `routeguard.start` | `{ waitSecs? }` launch service |

## Gap detection

Clients track `lastSeq`. If incoming `seq > lastSeq + 1`, request `agent.event_replay` for `(lastSeq, incomingSeq)`.
