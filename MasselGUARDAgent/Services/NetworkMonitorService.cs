using System;
using System.Threading;
using System.Net.NetworkInformation;
using MasselGUARD.Agent.Events;

namespace MasselGUARD.Agent.Services
{
    /// <summary>
    /// Publishes network availability/address change events via NetworkChange.
    /// </summary>
    public sealed class NetworkMonitorService : IDisposable
    {
        private readonly AgentEventBus _bus;
        private Timer? _debounce;
        private bool _available = true;

        public event Action? NetworkChanged;

        public NetworkMonitorService(AgentEventBus bus) => _bus = bus;

        public void Start()
        {
            _available = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            NetworkChange.NetworkAddressChanged += OnNetworkChange;
            NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChange;
        }

        private void OnNetworkChange(object? sender, EventArgs e) =>
            SchedulePublish("address");

        private void OnAvailabilityChange(object? sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
        {
            _available = e.IsAvailable;
            SchedulePublish("availability");
        }

        private void SchedulePublish(string changeKind)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ =>
            {
                _bus.Publish(AgentEventTypes.NetworkChanged, new
                {
                    available = _available,
                    changeKind,
                });
                NetworkChanged?.Invoke();
            }, null, 500, Timeout.Infinite);
        }

        public void Dispose()
        {
            _debounce?.Dispose();
            NetworkChange.NetworkAddressChanged -= OnNetworkChange;
            NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChange;
        }
    }
}
