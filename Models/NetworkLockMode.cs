using System;
using System.Text.Json.Serialization;

namespace MasselGUARD.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NetworkLockMode
    {
        Disabled,
        Auto,
        AlwaysOn,
    }

    public static class NetworkLockModeExtensions
    {
        public static string ToApiString(this NetworkLockMode mode) =>
            mode switch
            {
                NetworkLockMode.Auto      => "auto",
                NetworkLockMode.AlwaysOn  => "alwaysOn",
                _                         => "disabled",
            };

        public static NetworkLockMode FromApiString(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                "auto"      => NetworkLockMode.Auto,
                "alwayson"  => NetworkLockMode.AlwaysOn,
                "always_on" => NetworkLockMode.AlwaysOn,
                _           => NetworkLockMode.Disabled,
            };

        public static bool RequiresEnforcementWhenActive(NetworkLockMode mode) =>
            mode is NetworkLockMode.Auto or NetworkLockMode.AlwaysOn;
    }
}
