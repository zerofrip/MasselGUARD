using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MasselGUARD.Agent.Events
{
    public sealed class SubscribeMessage
    {
        public string Op { get; set; } = "";
        public int Version { get; set; } = 1;
        public ulong SinceSeq { get; set; }
        public List<string> Filters { get; set; } = new();

        public static SubscribeMessage? TryParse(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > SubscriptionFilter.MaxLineBytes)
                return null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var op = root.TryGetProperty("op", out var o) ? o.GetString() : null;
                if (string.IsNullOrEmpty(op)) return null;

                var msg = new SubscribeMessage { Op = op };
                if (root.TryGetProperty("version", out var v) && v.TryGetInt32(out var ver))
                    msg.Version = ver;
                if (root.TryGetProperty("sinceSeq", out var s) && s.TryGetUInt64(out var since))
                    msg.SinceSeq = since;
                if (root.TryGetProperty("filters", out var f) && f.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in f.EnumerateArray())
                    {
                        var p = item.GetString();
                        if (!string.IsNullOrWhiteSpace(p))
                            msg.Filters.Add(p);
                    }
                }
                return msg;
            }
            catch
            {
                return null;
            }
        }

        public string ToAckJson(ulong snapshotSeq, int replayCount, ulong? replayFrom)
        {
            return JsonSerializer.Serialize(new
            {
                op = Op == "subscribe" ? "subscribed" : "filters_updated",
                version = 1,
                snapshotSeq,
                replayFrom,
                replayCount,
            });
        }
    }
}
