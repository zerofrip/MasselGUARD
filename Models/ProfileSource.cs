using System;
using System.Text.Json.Serialization;

namespace MasselGUARD.Models
{
    /// <summary>User-facing tunnel profile classification (distinct from connect-routing Source string).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProfileSource
    {
        Local,
        Companion,
        Imported,
        Managed,
    }

    public static class ProfileSourceExtensions
    {
        /// <summary>Derive ProfileSource from legacy connect-routing Source when not explicitly set.</summary>
        public static ProfileSource FromLegacySource(string source, DateTime? importedAt = null)
        {
            if (string.Equals(source, "managed", StringComparison.OrdinalIgnoreCase))
                return ProfileSource.Managed;
            if (!string.Equals(source, "local", StringComparison.OrdinalIgnoreCase))
                return ProfileSource.Companion;
            return importedAt.HasValue ? ProfileSource.Imported : ProfileSource.Local;
        }

        public static string ToApiString(ProfileSource ps) =>
            ps switch
            {
                ProfileSource.Companion => "companion",
                ProfileSource.Imported  => "imported",
                ProfileSource.Managed   => "managed",
                _                       => "local",
            };

        public static bool TryParseApi(string? value, out ProfileSource result)
        {
            result = ProfileSource.Local;
            if (string.IsNullOrWhiteSpace(value)) return false;
            switch (value.Trim().ToLowerInvariant())
            {
                case "local":      result = ProfileSource.Local;      return true;
                case "companion":  result = ProfileSource.Companion;  return true;
                case "imported":   result = ProfileSource.Imported;   return true;
                case "managed":    result = ProfileSource.Managed;    return true;
                default:           return false;
            }
        }

        /// <summary>Whether WireGuard .conf content may be edited via MasselGUARD.</summary>
        public static bool IsConfigEditable(ProfileSource ps) =>
            ps is ProfileSource.Local or ProfileSource.Imported;
    }
}
