using System;
using System.IO;
using System.Text.Json;

namespace MasselGUARD.Release
{
    /// <summary>Persists update apply outcomes for reliability dashboard.</summary>
    public static class UpdateHistoryStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        private static string HistoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MasselGUARD", "updates", "history.ndjson");

        public static void Record(UpdateApplyResult result, string channel)
        {
            try
            {
                var dir = Path.GetDirectoryName(HistoryPath)!;
                Directory.CreateDirectory(dir);
                var entry = new
                {
                    ts = DateTime.UtcNow.ToString("o"),
                    ok = result.Ok,
                    version = result.Version,
                    error = result.Error,
                    channel,
                };
                File.AppendAllText(HistoryPath, JsonSerializer.Serialize(entry, JsonOpts) + Environment.NewLine);
            }
            catch
            {
                /* non-critical */
            }
        }
    }
}
