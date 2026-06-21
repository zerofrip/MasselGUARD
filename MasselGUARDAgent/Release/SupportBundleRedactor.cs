using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MasselGUARD.Agent.Release
{
    /// <summary>JSON redaction rules per support bundle tier.</summary>
    public static class SupportBundleRedactor
    {
        private static readonly JsonSerializerOptions Pretty = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string RedactJson(string json, string tier)
        {
            if (string.Equals(tier, "full", StringComparison.OrdinalIgnoreCase))
                return json;

            try
            {
                var node = JsonNode.Parse(json);
                if (node == null) return json;
                RedactNode(node, tier, "");
                return node.ToJsonString(Pretty);
            }
            catch
            {
                return json;
            }
        }

        public static string RedactCrashDetail(string detail, string tier)
        {
            if (string.Equals(tier, "full", StringComparison.OrdinalIgnoreCase))
                return detail;
            if (string.Equals(tier, "support", StringComparison.OrdinalIgnoreCase))
                return TruncateStack(detail, 40);
            return HashValue(detail) + "\n" + FirstStackFrame(detail);
        }

        public static IReadOnlyList<string> RedactionNotes(string tier)
        {
            var notes = new List<string>
            {
                "WireGuard private keys excluded from all tiers",
            };
            if (string.Equals(tier, "sanitized", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add("endpoints removed");
                notes.Add("app rule paths basename only");
                notes.Add("crash stacks truncated");
                notes.Add("machine/user names hashed");
            }
            else if (string.Equals(tier, "support", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add("endpoints retained");
                notes.Add("SSID/WiFi history excluded");
                notes.Add("crash stacks truncated");
            }
            return notes;
        }

        private static void RedactNode(JsonNode node, string tier, string path)
        {
            if (node is JsonObject obj)
            {
                var keys = new List<string>(obj.Select(p => p.Key));
                foreach (var key in keys)
                {
                    var childPath = string.IsNullOrEmpty(path) ? key : $"{path}.{key}";
                    if (IsPrivateKeyField(key))
                    {
                        obj[key] = "<redacted>";
                        continue;
                    }

                    if (IsSanitizedOnly(tier))
                    {
                        if (IsEndpointField(key, childPath))
                        {
                            obj[key] = "<redacted>";
                            continue;
                        }
                        if (IsPathField(key) && obj[key]?.GetValueKind() == JsonValueKind.String)
                        {
                            obj[key] = PathBasename(obj[key]!.GetValue<string>());
                            continue;
                        }
                        if (IsIdentityField(key) && obj[key]?.GetValueKind() == JsonValueKind.String)
                        {
                            obj[key] = HashValue(obj[key]!.GetValue<string>());
                            continue;
                        }
                    }

                    if (obj[key] != null)
                        RedactNode(obj[key]!, tier, childPath);
                }
            }
            else if (node is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] != null)
                        RedactNode(arr[i]!, tier, $"{path}[{i}]");
                }
            }
        }

        private static bool IsSanitizedOnly(string tier) =>
            !string.Equals(tier, "support", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tier, "full", StringComparison.OrdinalIgnoreCase);

        private static bool IsPrivateKeyField(string key) =>
            key.Contains("private", StringComparison.OrdinalIgnoreCase)
            || key.Contains("preshared", StringComparison.OrdinalIgnoreCase)
            || key.Equals("psk", StringComparison.OrdinalIgnoreCase);

        private static bool IsEndpointField(string key, string path) =>
            key.Contains("endpoint", StringComparison.OrdinalIgnoreCase)
            || key.Equals("remoteIp", StringComparison.OrdinalIgnoreCase)
            || key.Equals("publicIp", StringComparison.OrdinalIgnoreCase)
            || path.Contains("peer", StringComparison.OrdinalIgnoreCase) && key.Equals("ip", StringComparison.OrdinalIgnoreCase);

        private static bool IsPathField(string key) =>
            key.Contains("path", StringComparison.OrdinalIgnoreCase)
            || key.Equals("appPath", StringComparison.OrdinalIgnoreCase);

        private static bool IsIdentityField(string key) =>
            key.Contains("user", StringComparison.OrdinalIgnoreCase)
            || key.Contains("machine", StringComparison.OrdinalIgnoreCase)
            || key.Contains("hostname", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ssid", StringComparison.OrdinalIgnoreCase);

        private static string PathBasename(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var idx = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return idx >= 0 ? path[(idx + 1)..] : path;
        }

        private static string HashValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return "sha256:" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }

        private static string FirstStackFrame(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";
            var m = Regex.Match(detail, @"^\s*at .+$", RegexOptions.Multiline);
            return m.Success ? m.Value.Trim() : "";
        }

        private static string TruncateStack(string detail, int maxLines)
        {
            if (string.IsNullOrEmpty(detail)) return detail;
            var lines = detail.Split('\n');
            if (lines.Length <= maxLines) return detail;
            return string.Join('\n', lines.AsSpan(0, maxLines).ToArray()) + "\n… truncated";
        }
    }
}
