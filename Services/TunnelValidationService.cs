using System;
using System.Collections.Generic;
using System.Linq;
using MasselGUARD.Cli;
using MasselGUARD.Models;

namespace MasselGUARD.Services
{
    public sealed class ValidationIssue
    {
        public string Field   { get; set; } = "";
        public string Code    { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Detail { get; set; }
    }

    public sealed class ValidationResult
    {
        public bool Valid { get; set; }
        public List<ValidationIssue> Errors { get; set; } = new();
    }

    /// <summary>Structured WireGuard + library validation for tunnel CRUD/import.</summary>
    public static class TunnelValidationService
    {
        public static ValidationResult ValidateConfig(string conf, string? excludeTunnelName = null,
            IEnumerable<StoredTunnel>? library = null, Func<StoredTunnel, string?>? decrypt = null)
        {
            var result = new ValidationResult { Valid = true };

            var wgErr = WireGuardConf.Validate(conf);
            if (wgErr != null)
            {
                result.Valid = false;
                result.Errors.Add(new ValidationIssue
                {
                    Field   = "config",
                    Code    = MapWgErrorCode(wgErr.Message),
                    Message = wgErr.Message,
                    Detail  = wgErr.Detail,
                });
            }

            if (library != null && decrypt != null)
                AppendDuplicatePublicKeyErrors(result, conf, excludeTunnelName, library, decrypt);

            result.Valid = result.Errors.Count == 0;
            return result;
        }

        public static ValidationResult ValidateName(string name, IEnumerable<StoredTunnel> library,
            string? excludeTunnelName = null)
        {
            var result = new ValidationResult { Valid = true };
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Valid = false;
                result.Errors.Add(new ValidationIssue
                {
                    Field = "name", Code = "missing_name",
                    Message = "Tunnel name is required.",
                });
                return result;
            }

            var sanitized = SanitizeTunnelName(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                result.Valid = false;
                result.Errors.Add(new ValidationIssue
                {
                    Field = "name", Code = "invalid_name",
                    Message = "Tunnel name contains invalid characters.",
                    Detail = "Use letters, numbers, spaces, hyphens, and underscores.",
                });
                return result;
            }

            if (library.Any(t => !string.Equals(t.Name, excludeTunnelName, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(t.Name, sanitized, StringComparison.OrdinalIgnoreCase)))
            {
                result.Valid = false;
                result.Errors.Add(new ValidationIssue
                {
                    Field = "name", Code = "duplicate_name",
                    Message = $"A tunnel named '{sanitized}' already exists.",
                    Detail = "Choose a different name or rename the existing tunnel.",
                });
            }

            return result;
        }

        public static ValidationResult ValidateDraft(
            string? name,
            string? conf,
            IEnumerable<StoredTunnel> library,
            string? excludeTunnelName = null,
            Func<StoredTunnel, string?>? decrypt = null)
        {
            var merged = new ValidationResult { Valid = true, Errors = new List<ValidationIssue>() };

            if (!string.IsNullOrEmpty(name))
            {
                var nameResult = ValidateName(name, library, excludeTunnelName);
                merged.Errors.AddRange(nameResult.Errors);
            }

            if (!string.IsNullOrEmpty(conf))
            {
                var confResult = ValidateConfig(conf, excludeTunnelName, library, decrypt);
                merged.Errors.AddRange(confResult.Errors);
            }

            merged.Valid = merged.Errors.Count == 0;
            return merged;
        }

        private static void AppendDuplicatePublicKeyErrors(
            ValidationResult result,
            string conf,
            string? excludeTunnelName,
            IEnumerable<StoredTunnel> library,
            Func<StoredTunnel, string?> decrypt)
        {
            var newKeys = WireGuardConf.ExtractPublicKeys(conf).ToHashSet(StringComparer.Ordinal);
            if (newKeys.Count == 0) return;

            foreach (var t in library)
            {
                if (!string.IsNullOrEmpty(excludeTunnelName)
                    && string.Equals(t.Name, excludeTunnelName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ProfileSourceExtensions.IsConfigEditable(t.ProfileSource)
                    && t.ProfileSource != ProfileSource.Companion)
                    continue;

                string? existingConf;
                try { existingConf = decrypt(t); }
                catch { continue; }
                if (string.IsNullOrEmpty(existingConf)) continue;

                foreach (var pk in WireGuardConf.ExtractPublicKeys(existingConf))
                {
                    if (!newKeys.Contains(pk)) continue;
                    result.Valid = false;
                    result.Errors.Add(new ValidationIssue
                    {
                        Field   = "peer.publicKey",
                        Code    = "duplicate_public_key",
                        Message = $"Public key already used by tunnel '{t.Name}'.",
                        Detail  = "Each peer public key should be unique across your profile library.",
                    });
                    return;
                }
            }
        }

        private static string MapWgErrorCode(string message)
        {
            if (message.Contains("Endpoint", StringComparison.OrdinalIgnoreCase)) return "invalid_endpoint";
            if (message.Contains("CIDR", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Address", StringComparison.OrdinalIgnoreCase)
                || message.Contains("AllowedIPs", StringComparison.OrdinalIgnoreCase))
                return "invalid_cidr";
            if (message.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase)
                || message.Contains("PublicKey", StringComparison.OrdinalIgnoreCase)
                || message.Contains("base64", StringComparison.OrdinalIgnoreCase))
                return "invalid_key";
            if (message.Contains("MTU", StringComparison.OrdinalIgnoreCase)) return "invalid_mtu";
            if (message.Contains("ListenPort", StringComparison.OrdinalIgnoreCase)) return "invalid_port";
            if (message.Contains("PersistentKeepalive", StringComparison.OrdinalIgnoreCase)) return "invalid_keepalive";
            if (message.Contains("empty", StringComparison.OrdinalIgnoreCase)) return "empty_config";
            return "invalid_config";
        }

        public static string SanitizeTunnelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Where(c => !invalid.Contains(c)).ToArray();
            var s = new string(chars).Trim();
            return string.IsNullOrEmpty(s) ? "" : s;
        }
    }
}
