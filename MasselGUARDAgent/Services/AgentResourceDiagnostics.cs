using System;
using System.Diagnostics;
using System.Linq;

namespace MasselGUARD.Agent.Services
{
    /// <summary>Local process resource snapshot for diagnostics / soak monitoring.</summary>
    public static class AgentResourceDiagnostics
    {
        public static object Collect()
        {
            var agent = SnapshotProcess(Process.GetCurrentProcess());
            var rg = Process.GetProcessesByName("routeguard-service")
                .Select(SnapshotProcess)
                .FirstOrDefault();

            return new
            {
                agent,
                routeguard = rg,
                ts = DateTime.UtcNow.ToString("o"),
            };
        }

        private static object? SnapshotProcess(Process? p)
        {
            if (p == null) return null;
            try
            {
                p.Refresh();
                return new
                {
                    pid = p.Id,
                    rssMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 2),
                    privateMb = Math.Round(p.PrivateMemorySize64 / (1024.0 * 1024.0), 2),
                    handles = p.HandleCount,
                    threads = p.Threads.Count,
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
