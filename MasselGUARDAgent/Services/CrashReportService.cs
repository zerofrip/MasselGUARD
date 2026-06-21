using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MasselGUARD;
using MasselGUARD.Models;
using MasselGUARD.Services;

namespace MasselGUARD.Agent.Services
{
    /// <summary>Local-first crash collection with opt-in upload (Phase 11).</summary>
    public sealed class CrashReportService
    {
        private readonly ConfigService _config;
        private readonly LogService _log;
        private readonly TelemetryRollupService? _telemetry;
        private readonly string _crashDir;

        public CrashReportService(ConfigService config, LogService log, TelemetryRollupService? telemetry = null)
        {
            _config = config;
            _log = log;
            _telemetry = telemetry;
            _crashDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MasselGUARD", "crashes");
            Directory.CreateDirectory(_crashDir);
        }

        public void InstallHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Record("unobserved_task", e.Exception?.ToString() ?? "unknown");
                e.SetObserved();
            };
        }

        private void OnUnhandled(object? sender, UnhandledExceptionEventArgs e)
        {
            Record("unhandled", e.ExceptionObject?.ToString() ?? "unknown", fatal: e.IsTerminating);
        }

        public string Record(string kind, string detail, bool fatal = false)
        {
            var id = Guid.NewGuid().ToString("N")[..12];
            var path = Path.Combine(_crashDir, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{kind}-{id}.json");
            var report = new
            {
                schemaVersion = 1,
                id,
                kind,
                fatal,
                ts = DateTime.UtcNow.ToString("o"),
                productVersion = UpdateChecker.CurrentVersionString,
                detail = Truncate(detail, 8192),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(report), Encoding.UTF8);
            _log.Warn($"[Crash] Recorded {kind} → {path}");
            _telemetry?.RecordCrash(kind);

            if (_config.Config.CrashReportingEnabled)
                _ = TryUploadAsync(path);

            return path;
        }

        public string CrashesDirectory => _crashDir;

        private async Task TryUploadAsync(string reportPath)
        {
            try
            {
                var url = string.IsNullOrWhiteSpace(_config.Config.CrashReportUrl)
                    ? "https://crash.masselguard.net/v1/report"
                    : _config.Config.CrashReportUrl;
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var json = await File.ReadAllTextAsync(reportPath).ConfigureAwait(false);
                using var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                await http.PostAsync(url, content).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn($"[Crash] Upload failed: {ex.Message}");
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s[..max];
        }
    }
}
