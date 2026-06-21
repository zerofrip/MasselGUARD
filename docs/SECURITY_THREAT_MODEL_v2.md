# MasselGUARD / RouteGuard — STRIDE threat model v2 (Phase 14)

Extends [SECURITY_THREAT_MODEL.md](SECURITY_THREAT_MODEL.md) with production hardening, chaos/soak, and PRS gates.

## Phase 14 surface updates

| Surface | Phase 13 state | Phase 14 additions |
|---------|----------------|-------------------|
| **IPC** | Beta AU full RPC; strict stable | Chaos C12 proves DoS resilience; RPC rate limits on destructive methods (design) |
| **Driver** | EV + attestation | Chaos C07 driver sim; driver absent must not bugcheck |
| **Updater** | Ed25519 fail-closed beta/stable | C11 rollback verification; manifest key rotation procedure |
| **Telemetry** | Allowlist + forbidden-key lint | Soak proves no endpoint leakage; ingest rate limits + auth token (optional) |
| **Support bundle** | 3-tier redaction + fuzz | Soak C12-style grep scan on bundles under load |
| **WFP NL** | Delegation optional | SCM `cleanup_stale()`; visible IPC failure (no silent degrade) |

## New threats from chaos / soak

| Threat | Mitigation |
|--------|------------|
| Chaos scripts left enabled on prod VM | `-WhatIf` default; require `MASSELGUARD_CHAOS=1` |
| Soak logs contain endpoints | Redact in `soak-report.json`; local-only artifacts |
| Resource metrics deanonymization | Aggregates only in upload batches |
| Long-run memory leak → DoS | RSS/handle monitoring + soak slope fail |
| Orphan WFP filters after crash | `cleanup_stale()` on RG service start + NL re-sync |

## Recovery & availability

| Threat | Mitigation |
|--------|------------|
| Silent WFP delegation failure | `RouteGuardEnforcer` publishes error + `network_lock.failure` telemetry |
| Transport infinite retry | Max 3 attempts; fatal → user notification |
| Stale NL after RG crash | `network_lock.recovered` within 60s SLO |

## PRS / release gates

Production releases require:

- PRS ≥ channel minimum ([PRODUCTION_READINESS_SCORE.md](PRODUCTION_READINESS_SCORE.md))
- Soak PASS for channel duration ([SOAK_TEST_PLAN.md](SOAK_TEST_PLAN.md))
- Chaos matrix PASS ([CHAOS_ENGINEERING.md](CHAOS_ENGINEERING.md))

## Priority mitigations (Phase 14)

1. WFP `cleanup_stale()` wired in SCM mode (RouteGuard)
2. WFP delegation IPC failure visible (Agent)
3. Chaos quick matrix in CI before stable
4. RPC rate limit on `update.apply` (design: 1 req/min per pipe client) — future
5. Telemetry ingest rate limit configured on server

## References

- [RECOVERY_GUARANTEES.md](RECOVERY_GUARANTEES.md)
- [RESOURCE_STABILITY.md](RESOURCE_STABILITY.md)
- [checklists/PRODUCTION_READINESS_CHECKLIST.md](checklists/PRODUCTION_READINESS_CHECKLIST.md)
- [checklists/SECURITY_CHECKLIST.md](checklists/SECURITY_CHECKLIST.md)
