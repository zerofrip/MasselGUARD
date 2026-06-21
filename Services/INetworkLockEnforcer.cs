using System.Collections.Generic;
using MasselGUARD.Models;

namespace MasselGUARD.Services
{
    public interface INetworkLockEnforcer
    {
        void Apply(NetworkLockPolicy policy, IReadOnlyList<TunnelAllowRule> tunnels);
        void RemoveAll();
        NetworkLockFilterSnapshot GetActiveFilters();
        void CleanupLegacyRules();
    }
}
