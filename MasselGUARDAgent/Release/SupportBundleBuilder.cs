using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using MasselGUARD;
using MasselGUARD.Agent.Events;
using MasselGUARD.Agent.Ipc.RouteGuard;
using MasselGUARD.Agent.Services;
using MasselGUARD.Models;
using MasselGUARD.Release;
using MasselGUARD.Services;

namespace MasselGUARD.Agent.Release
{
    public sealed class SupportBundleBuilder
    {
        private const long DefaultMaxBytes = 52_428_800;
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private readonly ConfigService _config;
        private readonly LogService _log;
        private readonly RouteGuardBridgeService _routeGuard;
        private readonly CrashReportService? _crashes;
        private readonly HistoryService _history;
        private readonly Func<object> _agentStatus;
        private readonly Func<IReadOnlyList<object>> _eventHistory;
        private readonly string _supportDir;

        private readonly object _progressLock = new();
        private SupportExportProgress? _activeProgress;

        public SupportBundleBuilder(
            ConfigService config,
            LogService log,
            RouteGuardBridgeService routeGuard,
            HistoryService history,
            CrashReportService? crashes,
            Func<object> agentStatus,
            Func<IReadOnlyList<object>> eventHistory)
        {
            _config = config;
            _log = log;
            _routeGuard = routeGuard;
            _history = history;
            _crashes = crashes;
            _agentStatus = agentStatus;
            _eventHistory = eventHistory;
            _supportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MasselGUARD", "support");
            Directory.CreateDirectory(_supportDir);
        }

        public object Export(SupportExportParams p)
        {
            var bundleId = Guid.NewGuid().ToString("N")[..12];
            var tier = NormalizeTier(p.Tier);
            var maxBytes = p.MaxSizeBytes > 0 ? p.MaxSizeBytes : DefaultMaxBytes;
            var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss");
            var workDir = Path.Combine(_supportDir, $"work-{bundleId}");
            var zipPath = Path.Combine(_supportDir, $"support_bundle-{bundleId}-{ts}.zip");

            if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
            Directory.CreateDirectory(workDir);

            var progress = new SupportExportProgress(bundleId, _supportDir);
            lock (_progressLock) { _activeProgress = progress; }

            var sections = new List<object>();
            var truncated = false;
            long totalBytes = 0;

            try
            {
                progress.Set("collecting_agent");

                var agentStatusJson = JsonSerializer.Serialize(_agentStatus(), JsonOpts);
                agentStatusJson = SupportBundleRedactor.RedactJson(agentStatusJson, tier);
                WriteSection(workDir, "agent-status.json", agentStatusJson, sections);

                var rgStatus = _routeGuard.GetStatus();
                var rgStatusJson = SupportBundleRedactor.RedactJson(
                    JsonSerializer.Serialize(rgStatus, JsonOpts), tier);
                WriteSection(workDir, "routeguard-status.json", rgStatusJson, sections);

                var updaterJson = JsonSerializer.Serialize(BuildUpdaterStatus(), JsonOpts);
                WriteSection(workDir, "updater-status.json", updaterJson, sections);

                if (p.IncludeEventHistory)
                {
                    var events = _eventHistory();
                    var capped = events.TakeLast(500).ToList();
                    var eventJson = SupportBundleRedactor.RedactJson(
                        JsonSerializer.Serialize(capped, JsonOpts), tier);
                    if (Encoding.UTF8.GetByteCount(eventJson) > 2 * 1024 * 1024)
                    {
                        capped = capped.TakeLast(200).ToList();
                        eventJson = JsonSerializer.Serialize(capped, JsonOpts);
                        truncated = true;
                    }
                    WriteSection(workDir, "event-history.json", eventJson, sections);
                }

                progress.Set("collecting_routeguard");

                object? observability = null;
                string? rgZipPath = null;
                try
                {
                    observability = _routeGuard.GetObservabilitySnapshot();
                    var obsJson = SupportBundleRedactor.RedactJson(
                        JsonSerializer.Serialize(observability, JsonOpts), tier);
                    WriteSection(workDir, "observability.json", obsJson, sections);
                }
                catch (Exception ex)
                {
                    sections.Add(new { id = "observability", status = "error", error = ex.Message });
                }

                try
                {
                    var rgExport = _routeGuard.ExportDiagnosticsRaw(tier);
                    rgZipPath = ExtractStringProp(rgExport, "path");
                    if (!string.IsNullOrEmpty(rgZipPath) && File.Exists(rgZipPath))
                    {
                        var rgBytes = new FileInfo(rgZipPath).Length;
                        if (ShouldEmbedRgBundle(tier) && rgBytes <= 30 * 1024 * 1024 && totalBytes + rgBytes < maxBytes)
                        {
                            File.Copy(rgZipPath, Path.Combine(workDir, "routeguard-bundle.zip"), overwrite: true);
                            sections.Add(new { id = "routeguard-bundle.zip", status = "ok", bytes = rgBytes });
                            totalBytes += rgBytes;
                        }
                        ExtractRgTail(rgZipPath, workDir);
                    }
                }
                catch (Exception ex)
                {
                    sections.Add(new { id = "routeguard-export", status = "error", error = ex.Message });
                }

                progress.Set("redacting");

                if (p.IncludeCrashReports)
                {
                    var crashDir = Path.Combine(workDir, "crash-reports");
                    Directory.CreateDirectory(crashDir);
                    var crashBytes = CopyCrashReports(crashDir, tier, maxBytes - totalBytes, out var crashTrunc);
                    truncated |= crashTrunc;
                    sections.Add(new { id = "crash-reports", status = "ok", bytes = crashBytes });
                    totalBytes += crashBytes;
                }

                var logsDir = Path.Combine(workDir, "logs");
                Directory.CreateDirectory(logsDir);
                File.WriteAllText(Path.Combine(logsDir, "agent-tail.txt"), _log.GetTailText());
                sections.Add(new { id = "logs/agent-tail.txt", status = "ok" });

                if (p.IncludeTunnelHistory && tier.Equals("full", StringComparison.OrdinalIgnoreCase))
                {
                    var hist = _history.Entries.Take(500).ToList();
                    var histJson = SupportBundleRedactor.RedactJson(
                        JsonSerializer.Serialize(hist, JsonOpts), tier);
                    WriteSection(workDir, Path.Combine("logs", "tunnel-history.json"), histJson, sections);
                }

                var diagnostics = BuildDiagnosticsSummary(observability, rgStatus, tier);
                WriteSection(workDir, "diagnostics.json", JsonSerializer.Serialize(diagnostics, JsonOpts), sections);

                progress.Set("zipping");

                var manifest = BuildManifest(bundleId, tier, sections, truncated, maxBytes);
                WriteSection(workDir, "manifest.json", JsonSerializer.Serialize(manifest, JsonOpts), sections);

                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(workDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                totalBytes = new FileInfo(zipPath).Length;
                if (totalBytes > maxBytes)
                    truncated = true;

                progress.Set("done");

                return new
                {
                    bundleId,
                    path = zipPath,
                    tier,
                    sizeBytes = totalBytes,
                    truncated,
                    sections,
                };
            }
            finally
            {
                try { Directory.Delete(workDir, true); } catch { /* best effort */ }
                lock (_progressLock) { _activeProgress = null; }
            }
        }

        public object? ExportStatus(string? exportId)
        {
            lock (_progressLock)
            {
                if (_activeProgress != null && (string.IsNullOrEmpty(exportId) || _activeProgress.ExportId == exportId))
                    return _activeProgress.Snapshot();
            }

            if (!string.IsNullOrEmpty(exportId))
            {
                var path = Path.Combine(_supportDir, $"export-{exportId}.progress.json");
                if (File.Exists(path))
                {
                    try
                    {
                        return JsonSerializer.Deserialize<object>(File.ReadAllText(path));
                    }
                    catch { /* fall through */ }
                }
            }

            return new { phase = "idle" };
        }

        private static bool ShouldEmbedRgBundle(string tier) =>
            tier.Equals("support", StringComparison.OrdinalIgnoreCase)
            || tier.Equals("full", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeTier(string? tier)
        {
            if (string.IsNullOrWhiteSpace(tier)) return "sanitized";
            tier = tier.Trim().ToLowerInvariant();
            return tier is "sanitized" or "support" or "full" ? tier : "sanitized";
        }

        private void WriteSection(string workDir, string relativePath, string content, List<object> sections)
        {
            var full = Path.Combine(workDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(full, content, Encoding.UTF8);
            var bytes = Encoding.UTF8.GetByteCount(content);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            sections.Add(new { path = relativePath.Replace('\\', '/'), sha256 = hash, bytes, status = "ok" });
        }

        private static void ExtractRgTail(string rgZipPath, string workDir)
        {
            var logsDir = Path.Combine(workDir, "logs");
            Directory.CreateDirectory(logsDir);
            try
            {
                using var archive = ZipFile.OpenRead(rgZipPath);
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Replace('\\', '/').EndsWith("logs/service-tail.txt", StringComparison.OrdinalIgnoreCase));
                if (entry == null) return;
                using var src = entry.Open();
                using var dst = File.Create(Path.Combine(logsDir, "routeguard-tail.txt"));
                src.CopyTo(dst);
            }
            catch { /* optional */ }
        }

        private long CopyCrashReports(string crashDir, string tier, long budget, out bool truncated)
        {
            truncated = false;
            if (_crashes == null || !Directory.Exists(_crashes.CrashesDirectory))
                return 0;

            long total = 0;
            var files = Directory.GetFiles(_crashes.CrashesDirectory, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(20);

            foreach (var src in files)
            {
                if (total >= budget || total >= 5 * 1024 * 1024) { truncated = true; break; }
                try
                {
                    var json = File.ReadAllText(src);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("detail", out var detailEl))
                    {
                        var detail = detailEl.GetString() ?? "";
                        var redacted = SupportBundleRedactor.RedactCrashDetail(detail, tier);
                        using var ms = new MemoryStream();
                        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                        {
                            w.WriteStartObject();
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                if (prop.NameEquals("detail"))
                                {
                                    w.WriteString("detail", redacted);
                                    continue;
                                }
                                prop.WriteTo(w);
                            }
                            w.WriteEndObject();
                        }
                        json = Encoding.UTF8.GetString(ms.ToArray());
                    }
                    var dest = Path.Combine(crashDir, Path.GetFileName(src));
                    File.WriteAllText(dest, json, Encoding.UTF8);
                    total += new FileInfo(dest).Length;
                }
                catch { /* skip bad file */ }
            }
            return total;
        }

        private object BuildUpdaterStatus()
        {
            var cfg = _config.Config;
            var updater = new UnifiedUpdateService(cfg);
            ReleaseManifest? manifest = null;
            try
            {
                manifest = updater.FetchManifestAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { /* offline */ }

            return new
            {
                channel = cfg.UpdateChannel,
                manifestUrl = cfg.UpdateManifestUrl ?? "https://releases.masselguard.net",
                currentVersion = UpdateChecker.CurrentVersionString,
                latestKnown = cfg.LatestKnownVersion,
                lastCheck = cfg.LastUpdateCheck == DateTime.MinValue ? null : cfg.LastUpdateCheck.ToString("o"),
                manifestAvailable = manifest != null,
                manifestVersion = manifest?.ProductVersion,
                backupRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MasselGUARD", "updates", "backup"),
            };
        }

        private static object BuildDiagnosticsSummary(object? observability, object rgStatus, string tier)
        {
            int? healthScore = null;
            try
            {
                var json = JsonSerializer.Serialize(observability);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("health", out var h)
                    && h.TryGetProperty("score", out var s) && s.TryGetInt32(out var score))
                    healthScore = score;
            }
            catch { /* optional */ }

            return new
            {
                schemaVersion = 1,
                tier,
                ts = DateTime.UtcNow.ToString("o"),
                healthScore,
                masselguardVersion = UpdateChecker.CurrentVersionString,
                routeguardStatus = rgStatus,
                flags = SupportBundleRedactor.RedactionNotes(tier),
            };
        }

        private object BuildManifest(string bundleId, string tier, List<object> sections, bool truncated, long maxBytes)
        {
            return new
            {
                schemaVersion = 1,
                bundleKind = "support_bundle",
                bundleId,
                tier,
                ts = DateTime.UtcNow.ToString("o"),
                redactionNotes = SupportBundleRedactor.RedactionNotes(tier),
                truncated,
                sizeBudgetBytes = maxBytes,
                componentVersions = new
                {
                    masselguard = UpdateChecker.CurrentVersionString,
                    masselguardAgent = UpdateChecker.CurrentVersionString,
                    routeguardService = "0.1.0",
                    releaseChannel = _config.Config.UpdateChannel,
                },
                sections,
            };
        }

        private static string? ExtractStringProp(object obj, string name)
        {
            try
            {
                var json = JsonSerializer.Serialize(obj);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(name, out var el))
                    return el.GetString();
            }
            catch { /* ignore */ }
            return null;
        }
    }

    public sealed class SupportExportParams
    {
        public string Tier { get; set; } = "sanitized";
        public bool IncludeCrashReports { get; set; }
        public bool IncludeEventHistory { get; set; } = true;
        public bool IncludeTunnelHistory { get; set; }
        public long MaxSizeBytes { get; set; } = 52_428_800;
    }

    internal sealed class SupportExportProgress
    {
        private readonly string _dir;
        private readonly object _lock = new();
        public string ExportId { get; }
        public string Phase { get; private set; } = "collecting_agent";
        public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

        public SupportExportProgress(string exportId, string supportDir)
        {
            ExportId = exportId;
            _dir = supportDir;
        }

        public void Set(string phase)
        {
            lock (_lock)
            {
                Phase = phase;
                UpdatedAt = DateTime.UtcNow;
                var path = Path.Combine(_dir, $"export-{ExportId}.progress.json");
                var json = JsonSerializer.Serialize(new { exportId = ExportId, phase, updatedAt = UpdatedAt.ToString("o") });
                File.WriteAllText(path, json);
            }
        }

        public object Snapshot()
        {
            lock (_lock)
            {
                return new { exportId = ExportId, phase = Phase, updatedAt = UpdatedAt.ToString("o") };
            }
        }
    }
}
