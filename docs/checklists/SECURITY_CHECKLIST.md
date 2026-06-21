# Security checklist

Extends [RELEASE.md](../RELEASE.md) security section.

- [ ] Pipe SDDL: default for beta; `MASSELGUARD_STRICT_PIPE=1` / `ROUTE_GUARD_STRICT_PIPE=1` for stable
- [ ] Diagnostics `full` tier gated (`ROUTE_GUARD_FULL_DIAGNOSTICS=1` + elevation)
- [ ] Support bundle redaction verified per tier (`tests/scripts/support_bundle_redaction.ps1`)
- [ ] No secrets in bundle (grep scan in CI or manual sample review)
- [ ] Telemetry opt-in default off; forbidden-key lint on upload batches
- [ ] `telemetry.summary` / Reliability page smoke on beta install
- [ ] `update.apply` requires manifest signature when pubkey configured
- [ ] Crash upload opt-in default off (`CrashReportingEnabled = false`)
- [ ] Support bundle crash inclusion separate consent (`SupportExportIncludeCrashes`)
- [ ] **Phase 14:** Chaos quick matrix PASS before stable (`MASSELGUARD_CHAOS=1`)
- [ ] **Phase 14:** WFP `cleanup_stale()` wired in RouteGuard SCM mode
- [ ] **Phase 14:** WFP delegation IPC failure visible (no silent `_active=false`)
- [ ] **Phase 14:** Telemetry ingest rate limit configured (if server deployed)
- [ ] **Phase 14:** Review [SECURITY_THREAT_MODEL_v2.md](../SECURITY_THREAT_MODEL_v2.md)
