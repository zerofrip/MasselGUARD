using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MasselGUARD.Models;

namespace MasselGUARD.Services
{
    public sealed class NetworkLockRuntimeState
    {
        public string Mode { get; set; } = "disabled";
        public bool EnforcementActive { get; set; }
        public List<string> ActiveTunnels { get; set; } = new();
        public DateTime? LastRecoveryAt { get; set; }
        public string? LastRecoveryReason { get; set; }
        public string? LastPolicyHash { get; set; }
    }

    public sealed class NetworkLockStateStore
    {
        private static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasselGUARD", "network_lock_state.json");

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private readonly object _lock = new();
        private NetworkLockRuntimeState _state = new();

        public NetworkLockRuntimeState Snapshot()
        {
            lock (_lock) return Clone(_state);
        }

        public void Load()
        {
            lock (_lock)
            {
                if (!File.Exists(StatePath))
                {
                    _state = new NetworkLockRuntimeState();
                    return;
                }
                try
                {
                    _state = JsonSerializer.Deserialize<NetworkLockRuntimeState>(
                        File.ReadAllText(StatePath), JsonOpts) ?? new NetworkLockRuntimeState();
                }
                catch
                {
                    _state = new NetworkLockRuntimeState();
                }
            }
        }

        public void Save(Action<NetworkLockRuntimeState> mutate)
        {
            lock (_lock)
            {
                mutate(_state);
                var dir = Path.GetDirectoryName(StatePath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(StatePath, JsonSerializer.Serialize(_state, JsonOpts));
            }
        }

        private static NetworkLockRuntimeState Clone(NetworkLockRuntimeState s) => new()
        {
            Mode               = s.Mode,
            EnforcementActive  = s.EnforcementActive,
            ActiveTunnels      = new List<string>(s.ActiveTunnels),
            LastRecoveryAt     = s.LastRecoveryAt,
            LastRecoveryReason = s.LastRecoveryReason,
            LastPolicyHash     = s.LastPolicyHash,
        };
    }
}
