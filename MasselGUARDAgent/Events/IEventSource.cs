namespace MasselGUARD.Agent.Events
{
    /// <summary>
    /// Extension point for external publishers (e.g. RouteGuard bridge) to emit
    /// into the agent event bus without coupling to Orchestrator.
    /// </summary>
    public interface IEventSource
    {
        void Publish(string type, object? payload);
    }

    /// <summary>Adapter that forwards publishes to AgentEventBus.</summary>
    public sealed class AgentEventSource : IEventSource
    {
        private readonly AgentEventBus _bus;

        public AgentEventSource(AgentEventBus bus) => _bus = bus;

        public void Publish(string type, object? payload) => _bus.Publish(type, payload);
    }
}
