# Production Readiness Score (PRS)

Composite score (0–100) gating beta and stable releases.

## Formula

```
PRS = 0.25·CrashFree + 0.20·Reconnect + 0.15·Update + 0.15·Driver + 0.15·Transport + 0.10·TelemetryHealth
```

| Sub-score | Weight | Source | Normalization |
|-----------|--------|--------|---------------|
| **CrashFree** | 25% | `telemetry.summary` crash-free rate (7d window / session) | ≥98% → 100; linear below |
| **Reconnect** | 20% | `soak-report.json` reconnect success rate | ≥95% → 100 |
| **Update** | 15% | update history success rate | ≥99% stable / ≥95% beta |
| **Driver** | 15% | chaos C07 pass | 100 pass / 0 fail |
| **Transport** | 15% | soak transport recovery success | ≥90% → 100 |
| **TelemetryHealth** | 10% | no forbidden-key violations; upload OK | binary 100/0 |

## Gate thresholds

| Channel | PRS minimum | Soak minimum | Chaos quick |
|---------|-------------|--------------|-------------|
| **Beta** | ≥ **85** | 7-day PASS | C02, C08, C09, C12 PASS |
| **Stable** | ≥ **92** | 14-day PASS | Full C01–C12 PASS |
| **Enterprise** (future) | ≥ **95** | 30-day PASS | Full + memory slope PASS |

## Computation

```powershell
./scripts/compute-prs.ps1 -Channel stable `
  -TelemetrySummaryPath reports/telemetry-summary.json `
  -SoakReportPath reports/soak/soak-report.json `
  -ChaosReportPath reports/chaos/chaos-report-latest.json `
  -ReportPath reports/prs
```

Output: `reports/prs/prs-{channel}.json` with `gatePass`, `subScores`, and `prs`.

Exit code **1** when below channel minimum.

## UI

Reliability page (`/reliability`) shows a **partial PRS estimate** from local crash-free + update metrics. Full gate requires soak + chaos artifacts via `compute-prs.ps1`.

## Pipeline

```
telemetry.summary + soak-report + chaos-report
        → compute-prs.ps1 → prs-{channel}.json → RELEASE_CHECKLIST gate
```

See [PRODUCTION_READINESS_CHECKLIST.md](checklists/PRODUCTION_READINESS_CHECKLIST.md).
