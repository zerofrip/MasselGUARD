# Telemetry privacy review (Phase 13)

## Summary

MasselGUARD optional telemetry collects **anonymous aggregate counters** only. It is **off by default** and independent of crash report upload and support bundle export.

## Data collected (when opted in)

- Random `installId` (UUID, per installation)
- Random `sessionId` (UUID, per agent run)
- Product version and release channel
- Platform (`win-x64`)
- Allowlisted metric names with enum dimensions (see [telemetry-schema-v1.md](telemetry-schema-v1.md))

## Data not collected

- WireGuard private keys, PSKs, peer public keys
- Tunnel endpoints, public IPs
- Tunnel names, SSIDs, WiFi history
- User names, machine names, file paths
- Stack traces or crash detail (crash upload is a separate opt-in)

## Legal basis

- **Consent** (GDPR Art. 6(1)(a)) — explicit toggle in Settings → Privacy
- No sale of data; no cross-app tracking

## User control

| Action | Effect |
|--------|--------|
| Enable telemetry | Sets `TelemetryConsentAt`; begins hourly rollup + upload |
| Disable telemetry | Stops upload; local rollups still written for Reliability page |
| Uninstall | Removes `%ProgramData%\MasselGUARD\` including telemetry |

## Retention

| Location | Retention |
|----------|-----------|
| Client `rollups.ndjson` | 30 days (rotation TBD in implementation) |
| Server ingest (if deployed) | 90 days aggregates |
| Crash upload (separate) | Per crash service policy |

## Security

- HTTPS POST only
- No authentication token (installId is not user identity)
- Server-side rate limiting and dimension cardinality caps required for production ingest
- Automated lint rejects forbidden key names in upload JSON

## DPIA triggers

A new Data Protection Impact Assessment is required if:

- Geo-IP or country inference is added
- Free-text diagnostic fields are included
- Telemetry is linked to license keys or accounts

## Subprocessors

Hosting/CDN for `telemetry.masselguard.net` (when deployed). List maintained in release documentation.

## Verification

- Unit tests on allowlist validation
- Manual review of sample batch from beta cohort before stable gate
- See [SECURITY_CHECKLIST.md](checklists/SECURITY_CHECKLIST.md)
