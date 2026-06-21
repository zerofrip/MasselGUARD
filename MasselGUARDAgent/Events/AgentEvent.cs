using System;

namespace MasselGUARD.Agent.Events
{
    public sealed class AgentEvent
    {
        public string Type { get; init; } = "";
        public object? Payload { get; init; }
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    }
}
