# Telemetry schema v1 (Phase 13)

Upload batches are **separate** from the agent event stream ([event-schema-v1.md](event-schema-v1.md)). They contain aggregates only — no raw events, no PII.

## Batch envelope

```json
{
  "schemaVersion": 1,
  "installId": "uuid-v4-per-install",
  "sessionId": "uuid-v4-per-agent-run",
  "productVersion": "3.6.0",
  "releaseChannel": "beta",
  "platform": "win-x64",
  "metrics": [
    { "name": "feature.used", "dims": { "feature": "network_lock" }, "count": 1 }
  ],
  "periodStart": "2026-06-20T00:00:00Z",
  "periodEnd": "2026-06-20T01:00:00Z"
}
```

## Allowed metrics (allowlist)

| Name | Dimensions | Description |
|------|------------|-------------|
| `feature.used` | `feature` enum | UI/feature usage |
| `crash.recorded` | `kind` enum | Local crash counter (not stack/content) |
| `update.check` | `available` true/false | Manifest check result |
| `update.apply` | `result` ok/fail/rollback | Apply outcome |
| `install.outcome` | `scenario`, `result` | Installer CA ingest |
| `driver.install` | `result` | Driver pnputil outcome |
| `network_lock.failure` | `reason` enum | NL enable/recover failure |
| `session.end` | `clean` true/false | Agent shutdown |
| `session.start` | — | Agent startup |

## Forbidden in upload payload

Tunnel names, endpoints, peer keys, SSIDs, file paths, stack traces, public IPs, machine names, free-text fields.

Validation: `TelemetryUploadService.ValidateNoForbiddenKeys` before POST.

## Local storage

| File | Purpose |
|------|---------|
| `%ProgramData%\MasselGUARD\telemetry\install-id.txt` | Anonymous install UUID |
| `%ProgramData%\MasselGUARD\telemetry\rollups.ndjson` | Hourly batch append |

## RPC

- `telemetry.summary` — local dashboard (no upload required)
