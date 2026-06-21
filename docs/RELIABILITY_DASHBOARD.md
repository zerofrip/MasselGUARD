# Reliability dashboard (Phase 13)

## Purpose

Release-quality metrics for beta/stable gates — complements the Diagnostics page (live health) with **session-level KPIs**.

## Metrics

| KPI | Source | Gate (beta / stable) |
|-----|--------|------------------------|
| Crash-free sessions | `TelemetryRollupService` session counters | ≥ 98% / ≥ 99.5% |
| Update success rate | `updates/history.ndjson` | ≥ 95% / ≥ 99% |
| Driver install rate | `installer/last-install.json` | ≥ 90% / ≥ 98% |
| Support bundle exports | `feature.used` counter | informational |
| Network Lock failures | `network_lock.failure` counter | informational |

## Architecture

```
Agent (TelemetryRollupService)
  → local rollups.ndjson
  → telemetry.summary RPC
  → /reliability UI

Opt-in upload → telemetry.masselguard.net/v1/events
  → nightly ETL → reports/reliability-{channel}.json
```

## UI

- Route: `/reliability` in masselguard-ui
- Tauri command: `telemetry_summary` → `telemetry.summary`

## Remote dashboard (optional)

See `tools/telemetry-ingest/` and `.github/workflows/telemetry-etl.yml` for nightly aggregation of opt-in uploads.
