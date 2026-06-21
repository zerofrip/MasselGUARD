# MasselGUARD SharpFuzz harnesses (Phase 13)

Requires [.NET 10 SDK](https://dotnet.microsoft.com/) and [libFuzzer](https://llvm.org/docs/LibFuzzer.html) via SharpFuzz on Windows.

## Projects

| Project | Target |
|---------|--------|
| `WireGuardConf.Fuzz` | `WireGuardConf.Parse` + `Validate` |
| `AgentIpc.Fuzz` | `JsonSerializer.Deserialize<IpcRequest>` |
| `SupportRedactor.Fuzz` | `SupportBundleRedactor.RedactJson` |

## Run (Windows + AFL++/libFuzzer installed)

```powershell
cd fuzz/WireGuardConf.Fuzz
dotnet build -c Release
# With SharpFuzz driver on PATH:
dotnet run -c Release
```

CI runs a 60s smoke build on `windows-latest` (see `.github/workflows/fuzz.yml`).
