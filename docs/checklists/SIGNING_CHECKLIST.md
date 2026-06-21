# Signing checklist

- [ ] EV code-signing certificate valid; timestamp server reachable
- [ ] All PE/DLL in `dist/` and RouteGuard `target/release/` signed
- [ ] `routeguard-callout.sys` + `.cat` signed; attestation complete (beta/stable)
- [ ] Manifest ed25519 signature embedded; public key in Agent `UnifiedUpdateService`
- [ ] Verify `ROUTE_GUARD_VERIFY_SIGNATURES=1` smoke on signed build
- [ ] SmartScreen submission (optional for beta)
