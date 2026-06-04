using System;
using System.Collections.Generic;
using System.Text;

namespace MasselGUARD.Cli
{
    /// <summary>
    /// Helpers for building and patching WireGuard .conf text.
    /// Used by CLI rawconnect (build from scratch) and connect overrides (patch existing).
    ///
    /// WireGuard .conf format:
    ///   [Interface]
    ///   PrivateKey = &lt;base64&gt;
    ///   Address    = 10.0.0.2/32
    ///   DNS        = 1.1.1.1
    ///
    ///   [Peer]
    ///   PublicKey       = &lt;base64&gt;
    ///   PresharedKey    = &lt;base64&gt;   (optional)
    ///   Endpoint        = host:port
    ///   AllowedIPs      = 0.0.0.0/0, ::/0
    ///   PersistentKeepalive = 25      (optional)
    /// </summary>
    internal static class WireGuardConf
    {
        // ── Known field → section mapping (case-insensitive key) ─────────────
        private static readonly HashSet<string> InterfaceFields =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "PrivateKey", "Address", "DNS", "ListenPort",
                "Table", "MTU", "PreUp", "PostUp", "PreDown", "PostDown",
            };

        private static readonly HashSet<string> PeerFields =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "PublicKey", "PresharedKey", "Endpoint", "AllowedIPs",
                "PersistentKeepalive",
            };

        // ── Build a .conf from scratch ────────────────────────────────────────

        /// <summary>
        /// Builds a complete WireGuard .conf text from the supplied parameters.
        /// Only non-null/non-empty values are written.
        /// </summary>
        public static string Build(
            string  privateKey,
            string  publicKey,
            string  endpoint,
            string? address      = null,
            string? dns          = null,
            string? allowedIPs   = null,
            string? presharedKey = null,
            int?    keepalive    = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[Interface]");
            sb.AppendLine($"PrivateKey = {privateKey.Trim()}");
            if (!string.IsNullOrWhiteSpace(address))
                sb.AppendLine($"Address = {address.Trim()}");
            if (!string.IsNullOrWhiteSpace(dns))
                sb.AppendLine($"DNS = {dns.Trim()}");

            sb.AppendLine();

            sb.AppendLine("[Peer]");
            sb.AppendLine($"PublicKey = {publicKey.Trim()}");
            if (!string.IsNullOrWhiteSpace(presharedKey))
                sb.AppendLine($"PresharedKey = {presharedKey.Trim()}");
            sb.AppendLine($"Endpoint = {endpoint.Trim()}");
            sb.AppendLine($"AllowedIPs = {(string.IsNullOrWhiteSpace(allowedIPs) ? "0.0.0.0/0, ::/0" : allowedIPs.Trim())}");
            if (keepalive.HasValue)
                sb.AppendLine($"PersistentKeepalive = {keepalive.Value}");

            return sb.ToString();
        }

        // ── Patch fields in an existing .conf ─────────────────────────────────

        /// <summary>
        /// Patches specific key=value pairs in an existing .conf string.
        /// Keys that already exist are replaced in-place.
        /// Keys that don't exist are appended to the correct section.
        /// Keys not in either known set are appended to [Interface].
        /// </summary>
        public static string Patch(string conf, Dictionary<string, string> overrides)
        {
            if (overrides.Count == 0) return conf;

            var lines      = new List<string>(conf.Split('\n'));
            var remaining  = new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);

            // First pass: replace existing lines
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].TrimStart();
                var eqPos   = trimmed.IndexOf('=');
                if (eqPos <= 0) continue;

                var key = trimmed[..eqPos].Trim();
                if (remaining.TryGetValue(key, out var newVal))
                {
                    lines[i] = $"{key} = {newVal}";
                    remaining.Remove(key);
                    if (remaining.Count == 0) break;
                }
            }

            // Second pass: append any fields that didn't exist yet
            if (remaining.Count > 0)
            {
                // Find section boundaries
                int interfaceLine = -1, peerLine = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    var t = lines[i].Trim();
                    if (t.Equals("[Interface]", StringComparison.OrdinalIgnoreCase)) interfaceLine = i;
                    if (t.Equals("[Peer]",      StringComparison.OrdinalIgnoreCase)) peerLine = i;
                }

                foreach (var kv in remaining)
                {
                    bool isPeer = PeerFields.Contains(kv.Key);
                    int  insertAfter;

                    if (isPeer && peerLine >= 0)
                    {
                        // Insert at end of [Peer] section (before next empty line or end of file)
                        insertAfter = FindSectionEnd(lines, peerLine);
                    }
                    else if (!isPeer && interfaceLine >= 0)
                    {
                        insertAfter = FindSectionEnd(lines, interfaceLine);
                    }
                    else
                    {
                        // Append to end
                        lines.Add($"{kv.Key} = {kv.Value}");
                        continue;
                    }

                    lines.Insert(insertAfter, $"{kv.Key} = {kv.Value}");
                }
            }

            return string.Join('\n', lines);
        }

        // ── Read a private key from --privkey or --privkeyfile ────────────────

        /// <summary>
        /// Resolves a private-key argument.
        /// If value starts with '@', treats the rest as a file path and reads from it.
        /// Otherwise returns the value directly (and warns that it appears in ps/logs).
        /// Returns (key, warning) where warning is non-null when the key was inline.
        /// </summary>
        public static (string key, string? warning) ResolveKey(string value)
        {
            if (value.StartsWith("@", StringComparison.Ordinal))
            {
                var path = value[1..].Trim();
                if (!System.IO.File.Exists(path))
                    throw new System.IO.FileNotFoundException($"Key file not found: {path}");
                return (System.IO.File.ReadAllText(path).Trim(), null);
            }
            return (value,
                "⚠  WARNING: private key supplied on the command line is visible in process " +
                "listings and shell history. Use --privkeyfile <path> for safer key loading.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static int FindSectionEnd(List<string> lines, int sectionStart)
        {
            for (int i = sectionStart + 1; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[")) return i;       // next section — insert before it
                if (string.IsNullOrEmpty(t)) return i; // blank line — insert before it
            }
            return lines.Count; // end of file
        }
    }
}
