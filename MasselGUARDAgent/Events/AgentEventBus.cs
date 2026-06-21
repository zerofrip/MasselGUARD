using System;

namespace MasselGUARD.Agent.Events
{
    /// <summary>In-process event bus — all publishers call Publish; AgentEventPublisher subscribes.</summary>
    public sealed class AgentEventBus
    {
        public event Action<AgentEvent>? EventPublished;

        public void Publish(string type, object? payload)
        {
            var evt = new AgentEvent
            {
                Type         = type,
                Payload      = payload,
                TimestampUtc = DateTime.UtcNow,
            };
            EventPublished?.Invoke(evt);
        }
    }
}
