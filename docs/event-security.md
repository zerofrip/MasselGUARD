# MasselGUARD Event System — Security Review (Phase 2)

## Threat model

The event stream is **local-machine only** via Windows named pipes. Remote attackers are out of scope; local malware with user privileges is the primary concern.

## Controls

| Area | Mitigation |
|------|------------|
| Pipe ACL | Standard named-pipe ACL; same trust boundary as RPC pipe |
| Subscribe input | Max line 4 KB; max 32 filter patterns; JSON parse errors ignored |
| Replay RPC | Rate-limited to 10 requests/second per agent |
| Ring buffer | Fixed capacity (64–4096); oldest events evicted — no unbounded memory |
| Secrets | Tunnel private keys and config blobs never appear on event bus |
| Version | Unknown `version > 1` rejected at parse time |
| Slow consumers | Failed pipe writes remove subscriber; no blocking on publish path |

## Audit checklist

- [ ] Verify no DPAPI blobs or `.conf` contents in event payloads
- [ ] Confirm `%APPDATA%\MasselGUARD\event_seq.json` contains only `{ lastSeq }`
- [ ] Test malformed subscribe JSON does not crash agent
- [ ] Test replay flood returns rate-limit error

## Migration

Phase 2 is additive: v0 clients ignoring `version`/`seq` continue to work. New clients should use v1 parsing and gap recovery.
