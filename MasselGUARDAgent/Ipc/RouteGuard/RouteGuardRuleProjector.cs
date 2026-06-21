using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasselGUARD.Models;

namespace MasselGUARD.Agent.Ipc.RouteGuard
{
    public sealed class TunnelContextDto
    {
        public string Name { get; set; } = "";
        public string? AdapterName { get; set; }
        public uint? IfIndex { get; set; }
        public string? EndpointIp { get; set; }
        public bool Connected { get; set; }
        public string? TransportKind { get; set; }
        public string? TransportRemote { get; set; }
    }

    /// <summary>
    /// Projects MasselGUARD SplitTunnelRules into RouteGuard routing.import_rules params.
    /// </summary>
    public static class RouteGuardRuleProjector
    {
        public static object BuildImportPayload(
            SplitTunnelRules rules,
            IReadOnlyList<TunnelContextDto> tunnelContexts)
        {
            var hasInclude = rules.AppRules.Any(r => r.Enabled && r.Route == "vpn");
            var mode = hasInclude ? "split_include" : "full_tunnel";

            var appRules = rules.AppRules
                .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.AppPath))
                .Select(r => new
                {
                    path = r.AppPath.Trim(),
                    mode = MapAppMode(r.Route, mode),
                    priority = (ushort)100,
                    enabled = true,
                })
                .ToList();

            var ipRules = rules.IpRules
                .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Cidr))
                .Select(r => new
                {
                    cidr = r.Cidr.Trim(),
                    target = MapTarget(r.Route),
                    priority = (ushort)100,
                    enabled = true,
                })
                .ToList();

            var domainRules = rules.DomainRules
                .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Pattern))
                .Select(r => new
                {
                    pattern = r.Pattern.Trim(),
                    target = MapTarget(r.Route),
                    priority = (ushort)100,
                    enabled = true,
                })
                .ToList();

            var active = tunnelContexts.FirstOrDefault(c => c.Connected);

            return new
            {
                clear = false,
                mode,
                appRules,
                ipRules,
                domainRules,
                tunnelContext = active == null ? null : new
                {
                    name = active.Name,
                    adapterName = active.AdapterName ?? active.Name,
                    ifIndex = active.IfIndex,
                    endpointIp = active.EndpointIp,
                    connected = active.Connected,
                    transportKind = active.TransportKind,
                    transportRemote = active.TransportRemote,
                },
            };
        }

        public static string ComputeHash(SplitTunnelRules rules, IReadOnlyList<TunnelContextDto> contexts)
        {
            var payload = BuildImportPayload(rules, contexts);
            var json = JsonSerializer.Serialize(payload);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hash)[..12].ToLowerInvariant();
        }

        private static string MapAppMode(string route, string routingMode)
        {
            if (routingMode == "split_include")
                return route == "vpn" ? "include" : "exclude";
            return route == "direct" ? "exclude" : "include";
        }

        private static string MapTarget(string route) =>
            route == "direct" ? "bypass" : "tunnel";
    }
}
