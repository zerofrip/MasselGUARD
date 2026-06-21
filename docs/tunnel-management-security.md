# Tunnel Management Security Review (Phase 3)

## Storage

- Tunnel `.conf` plaintext remains **DPAPI-encrypted** at `%APPDATA%\MasselGUARD\tunnels\*.conf.dpapi`.
- `config.json` stores metadata only; inline `Config` blobs continue to migrate to files on load.

## Secrets in transit and UI

- **Sanitized export** redacts `PrivateKey` and `PresharedKey` before clipboard/file output.
- **QR / full export** includes private keys; UI requires explicit confirmation before copy.
- **Event bus** publishes tunnel summary metadata only — never full config or keys.
- **Companion / Managed** profiles: agent rejects config writes; UI disables structured config fields.

## Validation

- Server-side `tunnel.validate` wraps `WireGuardConf.Validate` plus library duplicate name/public-key checks.
- Import and create paths validate **before** `SaveConfigToFile`.
- Tunnel names sanitized via `SanitizeName` / invalid filename character stripping.

## Active tunnel edits

- Config fields are read-only while a tunnel is connected (Local/Imported) to avoid mid-session key rotation races.
- Metadata (group, tags, favorite, notes) remains editable for Companion profiles.

## Recommendations

- Clear clipboard after sharing QR payloads when possible (OS-dependent; not enforced).
- Do not export sanitized configs back into MasselGUARD as imports without replacing redacted keys.

## Manual test matrix

1. Create local tunnel → connect → verify config read-only → disconnect → edit → save
2. Import `.conf` via drag-drop; duplicate name and duplicate public key errors
3. Export sanitized (no `PrivateKey`) vs QR (full, with confirmation)
4. Clone companion profile (metadata only; config view read-only)
5. Library search, sort, favorite, archive
6. History filter, CSV/JSON export, clear; failure reason on failed connect
7. Dashboard recent/favorites update via events without manual refresh
8. Legacy `config.json` loads; `tunnel.*` and `tunnels.*` RPC aliases return identical results
