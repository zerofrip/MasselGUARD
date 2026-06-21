using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MasselGUARD;
using MasselGUARD.Models;
using MasselGUARD.Services;

namespace MasselGUARD.Agent.Services
{
    /// <summary>Local-first anonymous metric rollups (Phase 13). Always writes locally; upload is opt-in.</summary>
    public sealed class TelemetryRollupService
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        private readonly ConfigService _config;
        private readonly LogService _log;
        private readonly string _telemetryDir;
        private readonly string _installIdPath;
        private readonly string _sessionId;
        private readonly ConcurrentDictionary<string, long> _counters = new();
        private readonly object _flushLock = new();
        private long _sessionsStarted;
        private long _sessionsClean;

        public TelemetryRollupService(ConfigService config, LogService log)
        {
            _config = config;
            _log = log;
            _telemetryDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MasselGUARD", "telemetry");
            _installIdPath = Path.Combine(_telemetryDir, "install-id.txt");
            Directory.CreateDirectory(_telemetryDir);
            _sessionId = Guid.NewGuid().ToString("N");
            _sessionsStarted = 1;
            RecordSessionStart();
        }

        public string InstallId => LoadOrCreateInstallId();
        public string SessionId => _sessionId;

        public void Record(string name, IReadOnlyDictionary<string, string>? dims = null, long delta = 1)
        {
            var key = MetricKey(name, dims);
            _counters.AddOrUpdate(key, delta, (_, v) => v + delta);
        }

        public void RecordFeatureUsed(string feature) =>
            Record("feature.used", new Dictionary<string, string> { ["feature"] = feature });

        public void RecordUpdateCheck(bool available) =>
            Record("update.check", new Dictionary<string, string> { ["available"] = available ? "true" : "false" });

        public void RecordUpdateApply(string result) =>
            Record("update.apply", new Dictionary<string, string> { ["result"] = result });

        public void RecordCrash(string kind) =>
            Record("crash.recorded", new Dictionary<string, string> { ["kind"] = kind });

        public void RecordNetworkLockFailure(string reason) =>
            Record("network_lock.failure", new Dictionary<string, string> { ["reason"] = reason });

        public void RecordSupportExport() => Record("feature.used", new Dictionary<string, string> { ["feature"] = "support_export" });

        public void RecordSessionEnd(bool clean)
        {
            if (clean) Interlocked.Increment(ref _sessionsClean);
            Record("session.end", new Dictionary<string, string> { ["clean"] = clean ? "true" : "false" });
            FlushHourlyRollup();
        }

        public object Summary()
        {
            var crashDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MasselGUARD", "crashes");
            var crashCount = Directory.Exists(crashDir)
                ? Directory.GetFiles(crashDir, "*.json").Length
                : 0;

            var updateHistory = LoadUpdateHistorySummary();
            var installOutcome = LoadInstallOutcome();

            return new
            {
                schemaVersion = 1,
                installId = InstallId,
                sessionId = _sessionId,
                productVersion = UpdateChecker.CurrentVersionString,
                releaseChannel = _config.Config.UpdateChannel,
                telemetryEnabled = _config.Config.TelemetryEnabled,
                sessionsStarted = _sessionsStarted,
                sessionsClean = _sessionsClean,
                crashFreeSessionRate = _sessionsStarted > 0
                    ? Math.Round(100.0 * _sessionsClean / _sessionsStarted, 2)
                    : (double?)null,
                localCrashFiles = crashCount,
                counters = SnapshotCounters(),
                updateHistory,
                installOutcome,
            };
        }

        public TelemetryBatch BuildUploadBatch()
        {
            var now = DateTime.UtcNow;
            var periodStart = now.AddHours(-1);
            return new TelemetryBatch
            {
                SchemaVersion = 1,
                InstallId = InstallId,
                SessionId = _sessionId,
                ProductVersion = UpdateChecker.CurrentVersionString,
                ReleaseChannel = _config.Config.UpdateChannel ?? "beta",
                Platform = "win-x64",
                PeriodStart = periodStart.ToString("o"),
                PeriodEnd = now.ToString("o"),
                Metrics = SnapshotCounters().Select(c => new TelemetryMetric
                {
                    Name = c.name,
                    Dims = c.dims,
                    Count = c.count,
                }).ToList(),
            };
        }

        public void FlushHourlyRollup()
        {
            lock (_flushLock)
            {
                try
                {
                    var batch = BuildUploadBatch();
                    var line = JsonSerializer.Serialize(batch, JsonOpts);
                    File.AppendAllText(Path.Combine(_telemetryDir, "rollups.ndjson"), line + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    _log.Debug($"[Telemetry] Flush failed: {ex.Message}");
                }
            }
        }

        public void IngestInstallOutcome()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MasselGUARD", "installer", "last-install.json");
            if (!File.Exists(path)) return;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.TryGetProperty("result", out var r))
                    Record("install.outcome", new Dictionary<string, string>
                    {
                        ["scenario"] = root.TryGetProperty("scenario", out var s) ? s.GetString() ?? "unknown" : "unknown",
                        ["result"] = r.GetString() ?? "unknown",
                    });
            }
            catch { /* ignore */ }
        }

        private void RecordSessionStart() => Record("session.start", null);

        private string LoadOrCreateInstallId()
        {
            if (File.Exists(_installIdPath))
            {
                var id = File.ReadAllText(_installIdPath).Trim();
                if (!string.IsNullOrEmpty(id)) return id;
            }
            var newId = Guid.NewGuid().ToString("N");
            File.WriteAllText(_installIdPath, newId);
            return newId;
        }

        private static string MetricKey(string name, IReadOnlyDictionary<string, string>? dims)
        {
            if (dims == null || dims.Count == 0) return name;
            var parts = dims.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}");
            return name + "|" + string.Join(",", parts);
        }

        private List<(string name, Dictionary<string, string> dims, long count)> SnapshotCounters()
        {
            var list = new List<(string, Dictionary<string, string>, long)>();
            foreach (var kv in _counters)
            {
                var idx = kv.Key.IndexOf('|');
                if (idx < 0)
                {
                    list.Add((kv.Key, new Dictionary<string, string>(), kv.Value));
                    continue;
                }
                var name = kv.Key[..idx];
                var dims = new Dictionary<string, string>();
                foreach (var part in kv.Key[(idx + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq > 0) dims[part[..eq]] = part[(eq + 1)..];
                }
                list.Add((name, dims, kv.Value));
            }
            return list;
        }

        private object? LoadUpdateHistorySummary()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MasselGUARD", "updates", "history.ndjson");
            if (!File.Exists(path)) return null;
            long ok = 0, fail = 0;
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("ok", out var o) && o.GetBoolean()) ok++;
                    else fail++;
                }
                catch { /* skip */ }
            }
            var total = ok + fail;
            return new
            {
                attempts = total,
                success = ok,
                failed = fail,
                successRate = total > 0 ? Math.Round(100.0 * ok / total, 2) : (double?)null,
            };
        }

        private object? LoadInstallOutcome()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MasselGUARD", "installer", "last-install.json");
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<object>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class TelemetryBatch
    {
        public int SchemaVersion { get; set; } = 1;
        public string InstallId { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string ProductVersion { get; set; } = "";
        public string ReleaseChannel { get; set; } = "";
        public string Platform { get; set; } = "win-x64";
        public string PeriodStart { get; set; } = "";
        public string PeriodEnd { get; set; } = "";
        public List<TelemetryMetric> Metrics { get; set; } = new();
    }

    public sealed class TelemetryMetric
    {
        public string Name { get; set; } = "";
        public Dictionary<string, string> Dims { get; set; } = new();
        public long Count { get; set; }
    }
}
