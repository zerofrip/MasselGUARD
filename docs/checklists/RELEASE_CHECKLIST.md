# Release checklist

Use before tagging a beta or stable release.

- [ ] Run `scripts/bump-version.ps1` and sync all artifacts (`version.json`, `BUILD.bat`, npm, Agent csproj)
- [ ] `cargo test` (RouteGuard) green
- [ ] `npm run check` (masselguard-ui) green
- [ ] `tests/scripts/installer_matrix.ps1 -Scenario all` PASS on clean VM
- [ ] Manual matrix in [routeguard-integration.md](../routeguard-integration.md) PASS
- [ ] `tests/scripts/observability_matrix.ps1` PASS (if present)
- [ ] Nightly fuzz workflow green (RouteGuard + MasselGUARD harness build)
- [ ] `tests/scripts/stability_matrix.ps1 -Scenario S06,S07` PASS; full matrix before stable
- [ ] **Phase 14 — Chaos:** `tests/chaos/chaos_matrix.ps1 -Scenario quick` PASS (beta); full C01–C12 before stable ([CHAOS_ENGINEERING.md](../CHAOS_ENGINEERING.md))
- [ ] **Phase 14 — Soak:** 7-day soak PASS (beta) / 14-day (stable) — `tests/scripts/soak_runner.ps1` artifact ([SOAK_TEST_PLAN.md](../SOAK_TEST_PLAN.md))
- [ ] **Phase 14 — PRS:** `./scripts/compute-prs.ps1 -Channel {beta|stable}` exit 0 — beta ≥ 85, stable ≥ 92 ([PRODUCTION_READINESS_SCORE.md](../PRODUCTION_READINESS_SCORE.md))
- [ ] [PRODUCTION_READINESS_CHECKLIST.md](PRODUCTION_READINESS_CHECKLIST.md) signed off for channel
- [ ] Reliability gates (see [RELIABILITY_DASHBOARD.md](../RELIABILITY_DASHBOARD.md)): crash-free ≥ 98% (beta) / 99.5% (stable), update success ≥ 95% / 99%
- [ ] `MASSELGUARD_MANIFEST_PUBKEY_B64` configured for signed manifest verify
- [ ] Build MSI + Burn `setup.exe` via `installer/build-installer.ps1`
- [ ] Upload GitHub Release + CDN manifest
- [ ] Release notes include: driver reboot requirement, .NET 10 Desktop Runtime prereq, known issues
