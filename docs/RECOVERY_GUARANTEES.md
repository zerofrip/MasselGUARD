# Recovery guarantees (SLOs)

Formal recovery service-level objectives for MasselGUARD + RouteGuard production stack.

## Network Lock

| Guarantee | SLO | State leak rule | Status |
|-----------|-----|-----------------|--------|
| WF path: rules survive agent crash | **100%** until explicit disable | No duplicate NL rules after restart | Met |
| WF path: recover on agent start | **≤ 30s** to `networklock.recovered` | `network_lock_state.json` matches runtime | Met |
| WFP path: no orphan filters after clean disconnect | **100%** filters removed | JSON state cleared | Met on disconnect |
| WFP path: recover after RG crash | **≤ 60s** after service start | `cleanup_stale()` + re-enable | Met (SCM wired Phase 14) |
| WFP delegation: IPC failure visible | Alert within **10s** | Must not silently mark inactive | Met (telemetry + event Phase 14) |

Implementation: RouteGuard `cleanup_stale()` on SCM start → `network_lock.recovered` event; Agent `RouteGuardEnforcer` publishes delegation failure + `network_lock.failure` telemetry counter.

## DNS redirect

| Guarantee | SLO | Notes |
|-----------|-----|-------|
| Fail-open on callout error | **100%** packets permitted | By design — security tradeoff |
| Redirect restored after RG restart | **≤ 60s** when driver present | Requires driver + WFP path |
| Domain cache recovery | **≤ 30s** after restart | `routing.domain_recovered` event |

## Transport failover (RouteGuard)

| Guarantee | SLO | Notes |
|-----------|-----|-------|
| Connect-time fallback to DirectUDP | **≤ 5s** from connect start | When LWO/Phantun unavailable |
| LWO/Phantun relay recovery | **≤ 30s** per attempt | Max **3** attempts; backoff 5/10/15s |
| After fatal transport failure | Must **not** auto-switch transport kind | User must reconnect |
| Health poll interval | **5s** | `transport_health.rs` |

Constants: `MAX_RECOVERY_ATTEMPTS=3`, backoff `[5,10,15]s`.

## Tunnel auto-reconnect (MasselGUARD)

| Guarantee | SLO | Notes |
|-----------|-----|-------|
| Unintentional drop detection (local) | **≤ 30s** | Poll-based |
| Companion grace before reconnect | **2s** | Race with WireGuard app |
| Reconnect attempts | **3** with 5/10/15s backoff | Same as transport |
| Intentional disconnect never retried | **100%** | `_intentionalDisconnects` |

**State leak rule:** No stale open history entries; no duplicate tunnel SCM entries after failed reconnect.

## Verification

- Chaos matrix: [CHAOS_ENGINEERING.md](CHAOS_ENGINEERING.md)
- Soak metrics: [SOAK_TEST_PLAN.md](SOAK_TEST_PLAN.md)
- Production gate: [PRODUCTION_READINESS_SCORE.md](PRODUCTION_READINESS_SCORE.md)

**Out of scope v1:** Runtime transport kind switch (LWO→DirectUDP without full reconnect).
