# Soak test plan

Long-duration stability validation for production readiness (Phase 14).

## Harness

`tests/scripts/soak_runner.ps1` supervises continuous polling and scheduled fault probes.

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `-DurationDays` | 7 | 7 / 14 / 30 |
| `-PollIntervalSec` | 60 | Tunnel + health poll |
| `-NetworkEventIntervalHours` | 6 | C10-lite adapter toggle |
| `-SleepCycleIntervalHours` | 12 | S04 sleep/resume (manual on VM) |
| `-UpdateCycleDays` | 7 | Staged N→N+1 apply |
| `-TransportFallbackProbeHours` | 24 | C02-lite Phantun kill |
| `-EnableSleep` | off | Log sleep/resume cycles |

## Architecture

```
soak_runner.ps1 (supervisor)
  ├── every 60s: tunnel.status + observability.snapshot + resource_snapshot
  ├── every 6h: network_switch_stress (1 cycle, when MASSELGUARD_CHAOS=1)
  ├── every 12h: sleep/resume (manual / stub)
  ├── every 24h: phantun_kill probe (when chaos enabled)
  └── daily: soak-day-N.json artifact
```

## VM requirements

- Dedicated soak VM with daily snapshot baseline
- Log dir: `%ProgramData%\MasselGUARD\soak\`
- Reports: `reports/soak/soak-report.json`

## 30-day schedule

| Week | Focus | Events |
|------|-------|--------|
| W1 | Baseline uptime | Polls only |
| W2 | Network churn | + adapter/WiFi 4×/day |
| W3 | Sleep + service bounce | + sleep 2×/day; agent restart 1×/day |
| W4 | Update + transport | + one upgrade cycle; transport probes |

## Metrics & alert thresholds

| Metric | Source | Warning | Fail (soak) |
|--------|--------|---------|-------------|
| `reconnect_count` | tunnel status transitions | > 10/day | sustained |
| `transport_recovery_count` | observability | > 5/day | sustained |
| `agent_rss_mb` slope | resource_snapshot | > 5 MB/h over 24h | > 50 MB/week |
| `routeguard_rss_mb` slope | resource_snapshot | > 10 MB/h | > 100 MB/week |
| `wfp_filter_count` | RG observability | +1 without NL toggle | monotonic increase |
| `health_score_min` | RG health | < 80 | < 70 for > 1h |

## Gate thresholds

| Channel | Minimum soak |
|---------|----------------|
| Beta | 7-day PASS |
| Stable | 14-day PASS |
| Enterprise (future) | 30-day PASS |

## Usage

```powershell
# 7-day beta soak
./tests/scripts/soak_runner.ps1 -DurationDays 7 -ReportPath reports/soak

# With chaos probes enabled
$env:MASSELGUARD_CHAOS = '1'
./tests/scripts/soak_runner.ps1 -DurationDays 14 -EnableSleep
```

## CI

[soak-long.yml](../.github/workflows/soak-long.yml) registers a Windows scheduled task for long runs and uploads daily artifacts.

See [RESOURCE_STABILITY.md](RESOURCE_STABILITY.md) for memory/handle thresholds.
