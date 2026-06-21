using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MasselGUARD.Agent.Events
{
    /// <summary>Wire-format event envelope (v1 canonical; v0 legacy supported on parse).</summary>
    public sealed class EventEnvelope
    {
        public const int CurrentEventSchemaVersion = 1;
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("seq")]
        public ulong? Seq { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("ts")]
        public string Ts { get; set; } = "";

        [JsonPropertyName("payload")]
        public object? Payload { get; set; }

        public static EventEnvelope CreateV1(ulong seq, string type, object? payload)
        {
            return new EventEnvelope
            {
                Version = CurrentEventSchemaVersion,
                Seq     = seq,
                Type    = type,
                Ts      = DateTime.UtcNow.ToString("o"),
                Payload = payload,
            };
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public string ToJsonLine() => JsonSerializer.Serialize(this, JsonOpts);

        public static EventEnvelope? TryParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var env = new EventEnvelope
                {
                    Type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                    Payload = root.TryGetProperty("payload", out var p)
                        ? JsonSerializer.Deserialize<object>(p.GetRawText())
                        : null,
                };

                if (root.TryGetProperty("version", out var v) && v.TryGetInt32(out var ver))
                    env.Version = ver;

                if (root.TryGetProperty("seq", out var s) && s.TryGetUInt64(out var seq))
                    env.Seq = seq;

                if (root.TryGetProperty("ts", out var ts))
                {
                    env.Ts = ts.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ts.GetInt64()).UtcDateTime.ToString("o")
                        : ts.GetString() ?? "";
                }

                return env;
            }
            catch
            {
                return null;
            }
        }
    }
}
