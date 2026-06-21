using System;
using System.IO;

namespace MasselGUARD.Release
{
    /// <summary>Ed25519 manifest verification keys (Phase 13).</summary>
    public static class ReleaseSigningKeys
    {
        /// <summary>
        /// Base64 Ed25519 public key for release manifest signatures.
        /// Override via MASSELGUARD_MANIFEST_PUBKEY_B64 for CI/dev.
        /// Production key is embedded at build time from release/manifest-pubkey.b64.
        /// </summary>
        public static byte[]? GetManifestPublicKey()
        {
            var env = Environment.GetEnvironmentVariable("MASSELGUARD_MANIFEST_PUBKEY_B64");
            if (!string.IsNullOrWhiteSpace(env))
            {
                try { return Convert.FromBase64String(env.Trim()); }
                catch { return null; }
            }

            // Placeholder 32-byte zero key disables verify until real key is shipped;
            // channels with verifySignatures require non-empty ManifestSignature + configured key.
            var embedded = Environment.GetEnvironmentVariable("MASSELGUARD_EMBEDDED_MANIFEST_PUBKEY_B64");
            if (!string.IsNullOrWhiteSpace(embedded))
            {
                try { return Convert.FromBase64String(embedded.Trim()); }
                catch { return null; }
            }

            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "release", "manifest-pubkey.b64");
                if (!File.Exists(path))
                {
                    var repoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "release", "manifest-pubkey.b64"));
                    if (File.Exists(repoPath)) path = repoPath;
                }
                if (File.Exists(path))
                {
                    var b64 = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(b64) && !b64.StartsWith('#'))
                        return Convert.FromBase64String(b64);
                }
            }
            catch { /* ignore */ }

            return null;
        }

        /// <summary>When true, unsigned manifests must be rejected.</summary>
        public static bool RequireSignedManifest(string channel)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("MASSELGUARD_REQUIRE_SIGNED_MANIFEST"), "1", StringComparison.Ordinal))
                return true;
            return channel.Equals("beta", StringComparison.OrdinalIgnoreCase)
                || channel.Equals("stable", StringComparison.OrdinalIgnoreCase);
        }
    }
}
