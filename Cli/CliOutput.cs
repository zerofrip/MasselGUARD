using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MasselGUARD.Cli
{
    /// <summary>
    /// Plain-text and JSON formatters for CLI output.
    /// All writes go to Console.Out (attached to the parent terminal via AttachConsole).
    /// </summary>
    internal static class CliOutput
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
        };

        // ── JSON ──────────────────────────────────────────────────────────────

        public static void PrintJson(object value)
            => Console.WriteLine(JsonSerializer.Serialize(value, JsonOpts));

        // ── Tunnel table (list command) ───────────────────────────────────────

        /// <param name="tunnels">(Name, Active, Source, Group)</param>
        public static void PrintTunnelTable(
            List<(string Name, bool Active, string Source, string Group)> tunnels)
        {
            if (tunnels.Count == 0)
            {
                Console.WriteLine("No tunnels configured.");
                return;
            }

            int nameW = Math.Max(tunnels.Max(t => t.Name.Length), 4) + 2;

            // Header
            Console.WriteLine($"{"NAME".PadRight(nameW)}{"STATUS",-14}SOURCE");
            Console.WriteLine(new string('─', nameW + 20));

            foreach (var (name, active, source, group) in tunnels)
            {
                string status = active ? "● Connected" : "○ Idle     ";
                Console.WriteLine($"{name.PadRight(nameW)}{status,-14}{source}");
            }

            int connectedCount = tunnels.Count(t => t.Active);
            Console.WriteLine();
            Console.WriteLine($"{connectedCount} of {tunnels.Count} tunnel(s) connected.");
        }

        // ── Generic result line ───────────────────────────────────────────────

        public static void Ok(string message)    => Console.WriteLine($"✓ {message}");
        public static void Error(string message) => Console.Error.WriteLine($"✗ {message}");
        public static void Info(string message)  => Console.WriteLine(message);
    }
}
