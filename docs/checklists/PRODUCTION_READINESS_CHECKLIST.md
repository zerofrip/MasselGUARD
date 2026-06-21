# Production readiness checklist

Use before promoting to **beta** or **stable**. Combines reliability, soak, chaos, and PRS gates.

## Beta (minimum)

- [ ] Phase 13 reliability gates: crash-free ≥ 98%, update success ≥ 95% ([RELIABILITY_DASHBOARD.md](../RELIABILITY_DASHBOARD.md))
- [ ] `tests/scripts/stability_matrix.ps1 -Scenario quick` PASS (S06, S07, S09)
- [ ] `tests/chaos/chaos_matrix.ps1 -Scenario quick` PASS (C02, C08, C09, C12) with `MASSELGUARD_CHAOS=1`
- [ ] 7-day soak PASS artifact: `reports/soak/soak-report.json` ([SOAK_TEST_PLAN.md](../SOAK_TEST_PLAN.md))
- [ ] `./scripts/compute-prs.ps1 -Channel beta` exit 0 (PRS ≥ **85**)
- [ ] WFP `cleanup_stale()` verified on RouteGuard SCM start ([RECOVERY_GUARANTEES.md](../RECOVERY_GUARANTEES.md))
- [ ] `agent.diagnostics.resources` RPC returns sane RSS/handle snapshot
- [ ] [SECURITY_CHECKLIST.md](SECURITY_CHECKLIST.md) complete

## Stable (additional)

- [ ] Crash-free ≥ 99.5%, update success ≥ 99%
- [ ] Full stability matrix S01–S10 reviewed (automated + manual sign-off)
- [ ] Full chaos matrix C01–C12 PASS
- [ ] 14-day soak PASS artifact
- [ ] `./scripts/compute-prs.ps1 -Channel stable` exit 0 (PRS ≥ **92**)
- [ ] Resource slope checks: Agent RSS ≤ 50 MB/week, RouteGuard ≤ 100 MB/week ([RESOURCE_STABILITY.md](../RESOURCE_STABILITY.md))
- [ ] [SECURITY_THREAT_MODEL_v2.md](../SECURITY_THREAT_MODEL_v2.md) reviewed
- [ ] Strict pipe ACL enabled for stable builds

## Enterprise (future)

- [ ] 30-day soak PASS
- [ ] PRS ≥ **95**
- [ ] Memory slope PASS on full chaos + soak artifacts

## Artifacts to attach to release

| Artifact | Path |
|----------|------|
| PRS report | `reports/prs/prs-{channel}.json` |
| Soak report | `reports/soak/soak-report.json` |
| Chaos report | `reports/chaos/chaos-report-*.json` |
| Stability report | `reports/stability/` |

## Commands (reference)

```powershell
./tests/chaos/chaos_matrix.ps1 -Scenario quick -ReportPath reports/chaos
./tests/scripts/soak_runner.ps1 -DurationDays 7 -ReportPath reports/soak
./scripts/compute-prs.ps1 -Channel beta -ChaosReportPath (Get-ChildItem reports/chaos/*.json | Sort-Object LastWriteTime -Desc | Select -First 1).FullName
```

See also [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
