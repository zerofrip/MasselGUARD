# Resource stability

Memory, handle, and WFP filter monitoring for production soak and chaos runs.

## Collection

| Component | Signals | Method |
|-----------|---------|--------|
| MasselGUARDAgent | WorkingSet64, handles, threads | `tests/chaos/lib/resource_snapshot.ps1` |
| RouteGuard service | RSS, threads | Same script (process name `routeguard-service`) |
| WFP filters | Active filter count | `agent.diagnostics.resources` RPC → RG observability |
| IPC | Agent pipe health | `agent.ping` during soak polls |

### RPC

```
agent.diagnostics.resources → {
  agent: { pid, rssMb, privateMb, handles, threads },
  routeguard: { process: { … }, wfpFilters: number | null },
  ts: ISO8601
}
```

Exposed on **Reliability** UI as "Resource health" panel.

## Thresholds

| Signal | Warning | Critical (soak fail) |
|--------|---------|----------------------|
| Agent RSS slope | > 5 MB/hour over 24h | > 20 MB/hour or > 50 MB/week |
| RouteGuard RSS slope | > 10 MB/hour | > 40 MB/hour or > 100 MB/week |
| Agent handle count | > 2000 | > 5000 or +500/day |
| WFP filters | +1 without NL toggle | +3 orphan / monotonic drift |
| Event ring dropped | any sustained | > 100/hour |

## Storage

- Local samples: append to `%ProgramData%\MasselGUARD\telemetry\resources.ndjson` (implementation: soak_runner polls)
- Upload: **daily aggregates only** in opt-in telemetry — no raw per-minute samples

## Scripts

```powershell
# Snapshot now
./tests/chaos/lib/resource_snapshot.ps1 -IncludeAgentRpc

# Via agent RPC
./tests/scripts/stability/Invoke-AgentRpc.ps1 -Method agent.diagnostics.resources
```

## UI

Reliability page (`/reliability`) shows Agent RSS, handles, RouteGuard RSS, and WFP filter count with 60s refresh.

See [SOAK_TEST_PLAN.md](SOAK_TEST_PLAN.md) for long-run gate integration.
