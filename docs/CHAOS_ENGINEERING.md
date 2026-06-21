# Chaos engineering

Controlled, reversible fault injection for MasselGUARD + RouteGuard on a dedicated Windows VM (`self-hosted, windows, vm`). **Never run chaos on developer machines or production cohorts.**

## Safety

- Injectors require `MASSELGUARD_CHAOS=1` (default is no-op / `-WhatIf`).
- RouteGuard-specific hooks may require `ROUTE_GUARD_CHAOS=1` on the service host.
- Use VM snapshots; restore baseline after full matrix runs.

## Layout

```
tests/chaos/
  chaos_matrix.ps1              # C01–C12 orchestrator
  lib/
    Invoke-AgentRpc.ps1         # forwards to tests/scripts/stability/
    Invoke-RgCli.ps1
    resource_snapshot.ps1
    Assert-Recovery.ps1
  injectors/
    transport_drop.ps1          # C01
    phantun_kill.ps1            # C02
    lwo_kill.ps1                # C03
    wfp_corrupt.ps1             # C05
    dns_failure.ps1             # C06
    driver_simulate.ps1         # C07
    agent_restart_loop.ps1      # C08
    routeguard_restart_loop.ps1 # C09
    network_switch_stress.ps1   # C10
    update_kill.ps1             # C11
    ipc_flood.ps1               # C12
  expected/
    recovery_profiles.json      # per-scenario SLO + failure class
```

## Scenario matrix

| ID | Fault | Max recovery | Class |
|----|-------|--------------|-------|
| C01 | DirectUDP path drop | 120s | `transient_network` |
| C02 | Phantun kill | 30s | `transport_recoverable` |
| C03 | LWO relay death | 30s | `transport_recoverable` |
| C04 | Transport exhausted (4× kill) | N/A (no auto-recover) | `transport_fatal` |
| C05 | WFP filter corruption | 60s | `network_lock_degraded` |
| C06 | DNS proxy failure | 10s detect | `dns_degraded` |
| C07 | Driver unavailable | 30s | `driver_degraded` |
| C08 | Agent restart loop | 30s/cycle | `agent_recoverable` |
| C09 | RouteGuard restart loop | 30s/cycle | `service_recoverable` |
| C10 | Network switch stress | 120s/switch | `network_churn` |
| C11 | Update mid-apply kill | 300s manual | `update_rollback` |
| C12 | IPC flood | 0 crash tolerance | `dos_resilience` |

Each run emits `chaos-report-*.json` with: `scenarioId`, `recoveryMs`, `failureClass`, `resourceSnapshot`, `observabilitySnapshot`, `PASS/FAIL`.

## Usage

```powershell
# PR gate (~15 min): C02, C08, C09, C12
$env:MASSELGUARD_CHAOS = '1'
./tests/chaos/chaos_matrix.ps1 -Scenario quick

# Pre-stable: full matrix
./tests/chaos/chaos_matrix.ps1 -Scenario all -ReportPath reports/chaos

# Dry run
./tests/chaos/chaos_matrix.ps1 -Scenario quick -WhatIf
```

## CI

- **PR / weekly:** `chaos_matrix.ps1 -Scenario quick` in [soak-long.yml](../.github/workflows/soak-long.yml)
- **Pre-stable:** full C01–C12 linked to PRS gate (see [PRODUCTION_READINESS_SCORE.md](PRODUCTION_READINESS_SCORE.md))

## Failure taxonomy

| Class | Auto-recover | User-visible |
|-------|--------------|--------------|
| `transient_network` | Yes (auto-reconnect) | Brief disconnect |
| `transport_recoverable` | Yes (3× restart) | Degraded transport |
| `transport_fatal` | No | Tunnel down |
| `network_lock_degraded` | Yes after restart | Possible leak if misconfigured |
| `dns_degraded` | Partial (fail-open) | DNS may bypass redirect |
| `driver_degraded` | Requires repair | Weak domain split-tunnel |
| `agent_recoverable` / `service_recoverable` | Yes | Brief RPC gap |
| `update_rollback` | Manual if rollback fails | Previous version restored |
| `dos_resilience` | Should not crash | Slow RPC |

See [RECOVERY_GUARANTEES.md](RECOVERY_GUARANTEES.md) for formal SLOs.
