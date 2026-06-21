# MasselGUARD Tauri GUI

Modern WireGuard management UI: **Tauri v2**, **Svelte 5**, **Tailwind CSS v4**.

## Architecture

```
Svelte → Tauri commands → MasselGUARDAgent (\\.\pipe\MasselGUARD) → Services → WireGuard
```

Shared components: [`packages/mg-ui-core/`](../packages/mg-ui-core/) (reusable with RouteGuard).

## Windows development

1. Publish agent: `dotnet publish MasselGUARDAgent/MasselGUARDAgent.csproj -c Release -o dist`
2. Run agent elevated: `dist\MasselGUARDAgent.exe`
3. UI dev: `npm install && npm run tauri dev`

Place `MasselGUARDAgent.exe` beside the Tauri dev binary or in `dist/`.

## Production

Run [`BUILD.bat`](../BUILD.bat) on Windows (.NET 10 + Node.js + Rust).

| Output | Role |
|---|---|
| `MasselGUARD.exe` | Tauri GUI (primary) |
| `MasselGUARDAgent.exe` | JSON-RPC sidecar |
| `MasselGUARD-legacy.exe` | Deprecated WPF |
| `MasselGUARDcli.exe` | CLI |
