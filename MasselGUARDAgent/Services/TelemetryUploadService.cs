using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MasselGUARD.Models;
using MasselGUARD.Services;

namespace MasselGUARD.Agent.Services
{
    /// <summary>Opt-in anonymous telemetry upload (Phase 13).</summary>
    public sealed class TelemetryUploadService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private readonly ConfigService _config;
        private readonly LogService _log;
        private readonly TelemetryRollupService _rollup;

        public TelemetryUploadService(ConfigService config, LogService log, TelemetryRollupService rollup)
        {
            _config = config;
            _log = log;
            _rollup = rollup;
        }

        public async Task TryUploadPendingAsync()
        {
            if (!_config.Config.TelemetryEnabled) return;

            var batch = _rollup.BuildUploadBatch();
            if (batch.Metrics.Count == 0) return;

            var url = string.IsNullOrWhiteSpace(_config.Config.TelemetryUploadUrl)
                ? "https://telemetry.masselguard.net/v1/events"
                : _config.Config.TelemetryUploadUrl;

            try
            {
                var json = JsonSerializer.Serialize(batch);
                ValidateNoForbiddenKeys(json);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                await Http.PostAsync(url, content).ConfigureAwait(false);
                _log.Debug("[Telemetry] Upload batch sent");
            }
            catch (Exception ex)
            {
                _log.Warn($"[Telemetry] Upload failed: {ex.Message}");
            }
        }

        private static void ValidateNoForbiddenKeys(string json)
        {
            var forbidden = new[] { "endpoint", "privatekey", "ssid", "tunnelname", "publicip", "machinename" };
            var lower = json.ToLowerInvariant();
            foreach (var f in forbidden)
            {
                if (lower.Contains($"\"{f}\""))
                    throw new InvalidOperationException($"Forbidden telemetry field: {f}");
            }
        }
    }
}
