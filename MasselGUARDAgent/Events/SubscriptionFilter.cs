using System;
using System.Collections.Generic;
using System.Linq;

namespace MasselGUARD.Agent.Events
{
    /// <summary>Wildcard subscription filter: tunnel.*, exact match, * (all).</summary>
    public sealed class SubscriptionFilter
    {
        public const int MaxPatterns = 32;
        public const int MaxLineBytes = 4096;

        private readonly List<string> _patterns = new();
        private readonly object _lock = new();

        public SubscriptionFilter(IEnumerable<string>? patterns = null)
        {
            if (patterns != null) SetPatterns(patterns);
        }

        public IReadOnlyList<string> Patterns
        {
            get { lock (_lock) return _patterns.ToList(); }
        }

        public void SetPatterns(IEnumerable<string> patterns)
        {
            lock (_lock)
            {
                _patterns.Clear();
                foreach (var p in patterns.Take(MaxPatterns))
                {
                    if (!string.IsNullOrWhiteSpace(p))
                        _patterns.Add(p.Trim());
                }
                if (_patterns.Count == 0)
                    _patterns.Add("*");
            }
        }

        /// <summary>Control-plane events always pass through filters.</summary>
        public bool Matches(string eventType)
        {
            if (IsControlPlane(eventType)) return true;

            lock (_lock)
            {
                if (_patterns.Count == 0) return true;
                foreach (var pattern in _patterns)
                {
                    if (MatchesPattern(pattern, eventType)) return true;
                }
                return false;
            }
        }

        public static bool IsControlPlane(string eventType) =>
            eventType.StartsWith("agent.", StringComparison.Ordinal);

        private static bool MatchesPattern(string pattern, string eventType)
        {
            if (pattern == "*") return true;
            if (pattern.EndsWith(".*", StringComparison.Ordinal))
            {
                var prefix = pattern[..^1];
                return eventType.StartsWith(prefix, StringComparison.Ordinal);
            }
            return string.Equals(pattern, eventType, StringComparison.Ordinal);
        }
    }
}
