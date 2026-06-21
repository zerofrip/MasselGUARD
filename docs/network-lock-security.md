# Network Lock — Security Model

Phase 4 introduces **Network Lock** as the sole traffic enforcement engine in MasselGUARDAgent. Per-tunnel `KillSwitch` flags remain in config as **Auto-mode triggers** only; firewall rules are applied exclusively through `NetworkLockService`.

## Modes

| Mode | Behavior |
|------|----------|
| **Disabled** | No firewall rules; outbound traffic follows Windows defaults |
| **Auto** | Enforces when any connected tunnel has `KillSwitch=true`, or legacy global `KillSwitchMode=always` |
| **Always On** | Enforces even without an active VPN tunnel |

## Traffic policy (Windows Firewall)

When enforcement is active, `FirewallEnforcer` applies `MasselGUARD_NL_*` rules:

**Allowed**

- Loopback (`127.0.0.0/8`, `::1/128`)
- Active VPN adapter(s) and WireGuard endpoint UDP
- DHCP (UDP 67/68) when `AllowDhcp=true` (default)
- DNS per `DnsPolicy`: `strict`, `allow_exceptions`, or `allow_dhcp`
- LAN CIDRs when `LanAccessEnabled=true`

**Blocked**

- Default outbound action = Block on Domain, Private, and Public profiles
- Non-VPN adapters (implicit — only explicit allows pass)

Legacy `MasselGUARD_KS_*` rules are cleaned on agent startup before re-application.

## Leak scenarios

| Scenario | Mitigation |
|----------|------------|
| Tunnel disconnect | Auto mode re-evaluates; rules removed when no qualifying tunnels remain |
| Reconnect window | Endpoint + adapter allow rules restored on connect before global block relaxes |
| Agent crash | Firewall rules persist; `network_lock_state.json` + `RecoverOnStartup()` re-applies |
| Windows reboot | WF rules survive reboot; gap until agent starts if not set to start with Windows |
| DNS leak | `strict` blocks outbound DNS except via VPN; use `allow_exceptions` for resolver IPs |
| IPv6 | Global outbound block applies to all profiles; diagnostics report leak protection status |
| LAN bleed | LAN traffic blocked unless `LanAccessEnabled` + configured CIDR allows |

## Recovery guarantees

1. **Persistent firewall rules** — Windows Firewall rules survive agent exit and reboot
2. **Runtime state file** — `%APPDATA%\MasselGUARD\network_lock_state.json` records mode, enforcement flag, active tunnels, recovery metadata
3. **Startup recovery** — Agent calls `RecoverOnStartup()` **before** orchestrator start; no unconditional `CleanupStaleRules()` that strips protection
4. **Event** — `networklock.recovered` emitted when rules are re-applied after restart

## Limitations

- **Pre-agent reboot gap** — If MasselGUARDAgent is not running at login, Always On rules from a prior session may persist but status RPC is unavailable until agent starts. Recommend “Start with Windows” for Always On users.
- **RouteGuard WFP delegation** — optional via `networkLockWfpDelegation`; when enabled, `RouteGuardEnforcer` replaces `FirewallEnforcer` (mutually exclusive). Split-tunnel bypass still requires RouteGuard routing sync.
- **WPF app** — Legacy WPF GUI still uses `KillSwitchService` directly; Tauri/agent path uses Network Lock + optional RouteGuard delegation.
- **Companion tunnels** — Endpoint IP may be unavailable for WireGuard-app tunnels; adapter allow still applies.

## RPC surface

| Canonical | Alias |
|-----------|-------|
| `networklock.status` | `network_lock.get` |
| `networklock.set` | `network_lock.set` |
| `networklock.enable` | — |
| `networklock.disable` | — |
| `networklock.set_mode` | — |
| `networklock.set_lan_access` | — |
| `networklock.set_dns_policy` | — |
| `killswitch.status` | thin wrapper → `networklock.status` |
| `killswitch.set` | thin wrapper → patch + re-evaluate |

## Manual test matrix (Windows, elevated agent)

1. **Auto + KillSwitch tunnel** — Connect tunnel with kill switch → verify Internet blocked → disconnect → verify release
2. **Always On** — Enable without tunnel → verify Internet blocked → add LAN exception → verify LAN works
3. **Agent crash** — Kill agent mid-session → restart → verify `networklock.recovered` + rules restored
4. **Reboot + Always On** — Before agent start: WF rules persist; after agent start: status OK
5. **DNS strict vs allow_exceptions** — Compare resolver behavior under enforcement
6. **Legacy RPC** — `network_lock.get/set`, `killswitch.status` return extended status
7. **No duplicate stacks** — Agent connect/disconnect does not double-apply KS + NL rules

## Automated tests (future)

- Policy evaluation unit tests (Auto triggers, AlwaysOn without tunnel)
- DHCP/DNS/LAN rule name generation
- State file round-trip
- RPC alias parity integration tests
