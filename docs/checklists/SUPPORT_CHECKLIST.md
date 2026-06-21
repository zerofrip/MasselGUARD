# Support checklist

- [ ] `support.export` reproduces on clean beta install (Diagnostics → Export support bundle)
- [ ] Tier `sanitized` safe to attach to ticket (manual review of sample bundle)
- [ ] Run `tests/scripts/support_bundle_redaction.ps1` PASS
- [ ] Playbook: interpret `diagnostics.json` health score + transport recovery (see [SUPPORT_PLAYBOOK.md](../SUPPORT_PLAYBOOK.md))
- [ ] Playbook: `updater-status.json` rollback path documented
- [ ] Known false positives documented (driver absent, RouteGuard not running)
- [ ] Escalation: when to request `support` tier re-export from user
