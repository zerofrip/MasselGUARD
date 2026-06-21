using System;
using MasselGUARD.Agent.Events;
using MasselGUARD.Agent.Ipc;
using MasselGUARD.Agent.Ipc.RouteGuard;
using MasselGUARD.Agent.Services;
using MasselGUARD.Services;

namespace MasselGUARD.Agent
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("MasselGUARDAgent starting…");

            var log           = new LogService();
            var history       = new HistoryService();
            var scripts       = new ScriptService();
            var config        = new ConfigService();
            var tunnels       = new TunnelService(log, scripts, history, killSwitch: null);
            var wifi          = new WiFiService();
            var rules         = new RuleEngine();
            var eventBus      = new AgentEventBus();
            var snapshotCache = new TunnelSnapshotCache();
            var firewall      = new FirewallEnforcer(log);
            var stateStore    = new NetworkLockStateStore();
            var routeGuardBridge = new RouteGuardBridgeService(config, log, eventBus);
            var routeGuardEnforcer = new RouteGuardEnforcer(eventBus, routeGuardBridge);
            var networkLock   = new NetworkLockService(config, tunnels, log, firewall, stateStore, eventBus, routeGuardEnforcer);

            RouteGuardBridge.Initialize(routeGuardBridge);

            config.Load();
            var telemetry = new TelemetryRollupService(config, log);
            telemetry.IngestInstallOutcome();
            var telemetryUpload = new TelemetryUploadService(config, log, telemetry);
            var crashReports = new CrashReportService(config, log, telemetry);
            crashReports.InstallHandlers();
            history.Load();
            history.LoadSsid();
            networkLock.RecoverOnStartup();
            routeGuardBridge.Start();
            routeGuardBridge.ReconcileOnStartup();

            history.CloseStaleHistoryEntries(name =>
            {
                var t = config.Config.Tunnels.Find(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                return t != null && tunnels.IsActive(t);
            });

            var seqStore = new EventSequenceStore();
            var seq      = new EventSequenceAllocator(seqStore);
            var metrics  = new EventStreamMetrics();
            var ring     = new EventRingBuffer(config.Config.EventRingSize);

            var orch = new Orchestrator(config, tunnels, log, wifi, rules, history, eventBus, snapshotCache, networkLock, routeGuardBridge);
            var rpc  = new RpcHandler(config, tunnels, history, networkLock, orch, wifi, rules, eventBus, routeGuardBridge, log, crashReports);
            var host = new AgentHost(rpc, ring, metrics);

            Func<ulong, object?> snapshotFactory = seqVal => rpc.BuildAgentSnapshot(seqVal);

            using var publisher = new AgentEventPublisher(
                eventBus,
                host.EventStream,
                seq,
                ring,
                metrics,
                snapshotFactory);

            rpc.SetPublisher(publisher);
            rpc.SetTelemetry(telemetry);
            routeGuardEnforcer.SetTelemetry(telemetry);

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                while (true)
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromHours(1));
                    telemetry.FlushHourlyRollup();
                    await telemetryUpload.TryUploadPendingAsync();
                }
            });
            using var netMonitor = new NetworkMonitorService(eventBus);
            netMonitor.NetworkChanged += () => orch.InvalidatePublicIpCache();

            orch.Start();
            host.Start();
            netMonitor.Start();

            log.Info($"Agent RPC on {IpcConstants.PipeName}, events on {IpcConstants.EventsPipeName}");

            var exit = new System.Threading.ManualResetEvent(false);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                telemetry.RecordSessionEnd(clean: true);
                publisher.Dispose();
                routeGuardBridge.Dispose();
                seq.Flush();
                exit.Set();
            };
            exit.WaitOne();

            host.Dispose();
            orch.Dispose();
        }
    }
}
