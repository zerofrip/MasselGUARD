using MasselGUARD.Agent.Events;
using MasselGUARD.Models;

namespace MasselGUARD.Agent.Ipc
{
    /// <summary>
    /// Backward-compatible static facade over <see cref="RouteGuard.RouteGuardBridgeService"/>.
    /// </summary>
    public static class RouteGuardBridge
    {
        private static RouteGuard.RouteGuardBridgeService? _service;

        public static void Initialize(RouteGuard.RouteGuardBridgeService service) => _service = service;

        public static void SetEventSource(IEventSource source) { /* legacy no-op; service uses AgentEventBus */ }

        public static bool IsAvailable() =>
            _service?.Availability == RouteGuard.RouteGuardAvailability.Running;

        public static void SyncAppRulesIfEnabled(AppConfig cfg)
        {
            if (!cfg.SplitTunnel.UseRouteGuardBridge) return;
            _service?.SyncIfEnabled();
        }

        public static void PublishRouteGuardEvent(string type, object? payload)
        {
            if (_service == null || !type.StartsWith("routeguard.", System.StringComparison.Ordinal))
                return;
            // Events are published directly by RouteGuardBridgeService.
        }

        public static object Status() => _service?.GetStatus() ?? new
        {
            availability = "absent",
            pipe = @"\\.\pipe\RouteGuard",
            experimental = true,
            eventBridgeReady = false,
        };
    }
}
