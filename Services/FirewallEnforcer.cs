using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MasselGUARD.Models;

namespace MasselGUARD.Services
{
    /// <summary>
    /// Windows Firewall enforcement for Network Lock (MasselGUARD_NL_ rules).
    /// </summary>
    public sealed class FirewallEnforcer : INetworkLockEnforcer
    {
        private readonly LogService _log;
        private readonly object _lock = new();
        private bool _globalBlockActive;
        private int _savedDomain = 1;
        private int _savedPrivate = 1;
        private int _savedPublic = 1;
        private readonly List<string> _appliedRules = new();

        public const string Prefix = "MasselGUARD_NL_";
        private const string LegacyPrefix = "MasselGUARD_KS_";
        private const int AllProf = 2147483647;
        private const int DirOut = 2;
        private const int Allow = 1;
        private const int UDP = 17;
        private const int TCP = 6;
        private const int ProfDomain = 1;
        private const int ProfPrivate = 2;
        private const int ProfPublic = 4;

        public FirewallEnforcer(LogService log) => _log = log;

        public void Apply(NetworkLockPolicy policy, IReadOnlyList<TunnelAllowRule> tunnels)
        {
            lock (_lock)
            {
                RemoveAllInternal();
                if (!NetworkLockModeExtensions.RequiresEnforcementWhenActive(policy.Mode))
                    return;

                ApplyGlobalBlock();
                AddBaseAllows(policy);

                foreach (var t in tunnels)
                    AddTunnelRules(t);

                _log.Ok($"[NetworkLock] Applied — mode={policy.Mode}, tunnels={tunnels.Count}");
            }
        }

        public void RemoveAll()
        {
            lock (_lock) { RemoveAllInternal(); }
        }

        public NetworkLockFilterSnapshot GetActiveFilters()
        {
            lock (_lock)
            {
                return new NetworkLockFilterSnapshot
                {
                    ActiveRules       = new List<string>(_appliedRules),
                    GlobalBlockActive = _globalBlockActive,
                    LeakProtection    = _globalBlockActive ? "active" : "inactive",
                };
            }
        }

        public void CleanupLegacyRules()
        {
            try
            {
                var policy = OpenPolicy();
                if (policy == null) return;
                var rules = policy.Rules;
                var toRemove = new List<string>();
                foreach (dynamic rule in rules)
                {
                    try
                    {
                        string? rName = rule.Name;
                        if (rName != null &&
                            (rName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
                             rName.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase)))
                            toRemove.Add(rName);
                    }
                    catch { /* skip */ }
                }
                foreach (var n in toRemove)
                {
                    try { rules.Remove(n); } catch { }
                }
                if (toRemove.Count > 0)
                    _log.Debug($"[NetworkLock] Cleaned {toRemove.Count} stale rule(s).");
            }
            catch (Exception ex)
            {
                _log.Debug($"[NetworkLock] CleanupLegacyRules: {ex.Message}");
            }
        }

        public static string? ParseEndpointIp(string wireguardConfig)
        {
            var m = Regex.Match(wireguardConfig,
                @"^\s*Endpoint\s*=\s*\[?([0-9A-Fa-f:.]+)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        private void RemoveAllInternal()
        {
            try
            {
                var policy = OpenPolicy();
                if (policy != null)
                {
                    foreach (var name in _appliedRules.ToList())
                    {
                        try { policy.Rules.Remove(name); } catch { }
                    }
                    if (_globalBlockActive)
                        RestoreGlobalPolicy(policy);
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"[NetworkLock] RemoveAll: {ex.Message}");
            }
            _appliedRules.Clear();
            _globalBlockActive = false;
        }

        private void ApplyGlobalBlock()
        {
            var policy = OpenPolicy();
            if (policy == null) return;

            try { _savedDomain  = policy.DefaultOutboundAction[ProfDomain];  } catch { _savedDomain  = Allow; }
            try { _savedPrivate = policy.DefaultOutboundAction[ProfPrivate]; } catch { _savedPrivate = Allow; }
            try { _savedPublic  = policy.DefaultOutboundAction[ProfPublic];  } catch { _savedPublic  = Allow; }

            try { policy.DefaultOutboundAction[ProfDomain]  = 0; } catch { }
            try { policy.DefaultOutboundAction[ProfPrivate] = 0; } catch { }
            try { policy.DefaultOutboundAction[ProfPublic]  = 0; } catch { }

            _globalBlockActive = true;
        }

        private void RestoreGlobalPolicy(dynamic policy)
        {
            try { policy.DefaultOutboundAction[ProfDomain]  = _savedDomain;  } catch { }
            try { policy.DefaultOutboundAction[ProfPrivate] = _savedPrivate; } catch { }
            try { policy.DefaultOutboundAction[ProfPublic]  = _savedPublic;  } catch { }
        }

        private void AddBaseAllows(NetworkLockPolicy policy)
        {
            var policyObj = OpenPolicy();
            if (policyObj == null) return;
            var rules = policyObj.Rules;

            AddRule(rules, Prefix + "Allow_Loopback", AllProf, remoteAddr: "127.0.0.0/8,::1/128");

            if (policy.AllowDhcp)
            {
                AddRule(rules, Prefix + "Allow_DHCP", AllProf, remoteAddr: "255.255.255.255", protocol: UDP, localPorts: "68", remotePorts: "67");
                AddRule(rules, Prefix + "Allow_DHCP6", AllProf, remoteAddr: "ff02::1:2", protocol: UDP, localPorts: "546", remotePorts: "547");
            }

            if (policy.LanAccessEnabled)
            {
                foreach (var cidr in policy.LanExceptions.Where(c => !string.IsNullOrWhiteSpace(c)))
                    AddRule(rules, Prefix + "Allow_LAN_" + SanitizeRuleSuffix(cidr), AllProf, remoteAddr: cidr.Trim());
            }

            ApplyDnsRules(rules, policy);
        }

        private void ApplyDnsRules(dynamic rules, NetworkLockPolicy policy)
        {
            if (string.Equals(policy.DnsPolicy, "allow_dhcp", StringComparison.OrdinalIgnoreCase))
            {
                AddRule(rules, Prefix + "Allow_DNS_DHCP", AllProf, remoteAddr: "255.255.255.255", protocol: UDP, remotePorts: "53");
                return;
            }

            if (string.Equals(policy.DnsPolicy, "allow_exceptions", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var ip in policy.DnsExceptions.Where(d => !string.IsNullOrWhiteSpace(d)))
                {
                    AddRule(rules, Prefix + "Allow_DNS_" + SanitizeRuleSuffix(ip), AllProf, remoteAddr: ip.Trim(), protocol: UDP, remotePorts: "53");
                    AddRule(rules, Prefix + "Allow_DNS_TCP_" + SanitizeRuleSuffix(ip), AllProf, remoteAddr: ip.Trim(), protocol: TCP, remotePorts: "53");
                }
            }
        }

        private void AddTunnelRules(TunnelAllowRule tunnel)
        {
            var policy = OpenPolicy();
            if (policy == null) return;
            var rules = policy.Rules;
            var adapter = tunnel.AdapterName ?? tunnel.Name;

            AddRule(rules, Prefix + "Allow_WG_" + tunnel.Name, AllProf, iface: adapter);

            if (!string.IsNullOrEmpty(tunnel.EndpointIp))
                AddRule(rules, Prefix + "Allow_EP_" + tunnel.Name, AllProf, remoteAddr: tunnel.EndpointIp, protocol: UDP);
        }

        private void AddRule(dynamic rulesCollection, string name, int profiles,
            string? remoteAddr = null, string? iface = null, int protocol = 256,
            string? localPorts = null, string? remotePorts = null)
        {
            try
            {
                var ruleType = Type.GetTypeFromProgID("HNetCfg.FwRule");
                if (ruleType == null) return;
                dynamic rule = Activator.CreateInstance(ruleType)!;
                rule.Name = name;
                rule.Direction = DirOut;
                rule.Action = Allow;
                rule.Enabled = true;
                rule.Profiles = profiles;
                if (protocol != 256) rule.Protocol = protocol;
                if (remoteAddr != null) rule.RemoteAddresses = remoteAddr;
                if (iface != null) rule.Interfaces = new[] { iface };
                if (localPorts != null) rule.LocalPorts = localPorts;
                if (remotePorts != null) rule.RemotePorts = remotePorts;
                rulesCollection.Add(rule);
                _appliedRules.Add(name);
            }
            catch (Exception ex)
            {
                _log.Debug($"[NetworkLock] AddRule {name}: {ex.Message}");
            }
        }

        private static dynamic? OpenPolicy()
        {
            try
            {
                var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (type == null) return null;
                return Activator.CreateInstance(type);
            }
            catch { return null; }
        }

        private static string SanitizeRuleSuffix(string input)
        {
            var chars = input.Where(c => char.IsLetterOrDigit(c) || c is '.' or ':' or '/').ToArray();
            var s = new string(chars);
            return s.Length > 40 ? s[..40] : s;
        }
    }
}
