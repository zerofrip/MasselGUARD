using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using MasselGUARD.Models;
using MasselGUARD.Services;

namespace MasselGUARD.Cli
{
    /// <summary>
    /// CLI entry point. Invoked from Program.Main when command-line args are present.
    ///
    /// Runs entirely without WPF — no App, no MainWindow.
    /// Attaches to the parent console (cmd / PowerShell) so output appears inline.
    /// Coexists safely with a running GUI instance: both talk to the same WireGuard
    /// kernel driver; the GUI picks up state changes within ~1 second via its poll timer.
    ///
    /// Commands
    ///   list   / --list         All tunnels + connected/idle status
    ///   status / --status       Active tunnel count and names
    ///   connect  <name>         Connect a tunnel by name
    ///   connect  --default      Connect the configured default action tunnel
    ///   disconnect <name>       Disconnect a tunnel by name
    ///   disconnect-all          Disconnect all active tunnels
    ///   version / --version / -v  Version, build, author + update status
    ///   help    / --help    / -h  Command reference
    ///
    /// Flags (any command)
    ///   --json                 Machine-readable JSON output
    ///   --quiet / -q           No output — exit code only
    ///
    /// Exit codes
    ///   0  success
    ///   1  error (tunnel not found, connect failed, not elevated, etc.)
    ///   2  already in desired state
    /// </summary>
    internal static class CliRunner
    {
        // ── Console ownership detection ───────────────────────────────────────
        // Used to detect whether Windows created a fresh console for this elevated
        // process (UAC from a non-admin terminal) vs. a console inherited from an
        // already-elevated parent.  When we own the console alone we pause before
        // exiting so the user can read the output before the window closes.
        [DllImport("kernel32.dll")]
        private static extern uint GetConsoleProcessList(
            [Out] uint[] lpdwProcessList, uint dwProcessCount);

        private static bool IsIsolatedConsole()
        {
            try { var l = new uint[2]; return GetConsoleProcessList(l, 2) <= 1; }
            catch { return false; }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when args indicate a CLI invocation (not a /service dispatch).
        /// Called from Program.Main before any WPF initialisation.
        /// </summary>
        public static bool IsCliInvocation(string[] args)
        {
            if (args.Length == 0) return false;
            var first = args[0];
            return !string.Equals(first, "/service", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Runs the CLI command and returns the process exit code.</summary>
        public static int Run(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            bool json  = args.Any(a => string.Equals(a, "--json",  StringComparison.OrdinalIgnoreCase));
            bool quiet = args.Any(a => string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(a, "-q",      StringComparison.OrdinalIgnoreCase));

            string cmd = args[0].ToLowerInvariant();

            // ── Commands that need no config ──────────────────────────────────
            switch (cmd)
            {
                case "help":
                case "--help":
                case "-h":
                case "-?":
                    return CmdHelp();
            }

            // ── Load config for all remaining commands ─────────────────────────
            var configSvc = new ConfigService();
            configSvc.Load();
            var cfg = configSvc.Config;

            int exitCode;
            try
            {
                exitCode = cmd switch
                {
                    "list"           or "--list"    => CmdList(cfg, json),
                    "status"         or "--status"  => CmdStatus(cfg, json),
                    "connect"                       => CmdConnect(args, cfg, json, quiet),
                    "disconnect"                    => CmdDisconnect(args, cfg, json, quiet),
                    "disconnect-all"                => CmdDisconnectAll(cfg, json, quiet),
                    "version"        or "--version"
                                     or "-v"        => CmdVersion(cfg, json),
                    _ => UnknownCmd(cmd),
                };
            }
            catch (Exception ex)
            {
                CliOutput.Error($"Unexpected error: {ex.Message}");
                exitCode = 1;
            }

            Console.Out.Flush();

            // If Windows created a fresh console for this process (UAC elevation
            // from a non-admin terminal), the window closes the instant we exit and
            // the user sees nothing.  Detect sole console ownership and pause.
            // --quiet / -q suppresses the pause for scripted callers.
            if (!quiet && IsIsolatedConsole())
            {
                Console.Error.WriteLine();
                Console.Error.Write("[ Tip: run PowerShell as Administrator for inline output ]");
                Console.Error.WriteLine("  Press any key to close...");
                try { Console.ReadKey(true); } catch { }
            }

            return exitCode;
        }

        // ── list ──────────────────────────────────────────────────────────────

        private static int CmdList(AppConfig cfg, bool json)
        {
            var rows = cfg.Tunnels
                .Select(t => (t.Name, Active: IsActive(t), t.Source, t.Group))
                .ToList();

            if (json)
            {
                CliOutput.PrintJson(rows.Select(t => new
                {
                    name   = t.Name,
                    status = t.Active ? "connected" : "idle",
                    source = t.Source,
                    group  = string.IsNullOrEmpty(t.Group) ? null : (string?)t.Group,
                }));
            }
            else
            {
                CliOutput.PrintTunnelTable(rows);
            }
            return 0;
        }

        // ── status ────────────────────────────────────────────────────────────

        private static int CmdStatus(AppConfig cfg, bool json)
        {
            var active = cfg.Tunnels.Where(t => IsActive(t)).ToList();

            if (json)
            {
                CliOutput.PrintJson(new
                {
                    active_count = active.Count,
                    tunnels      = active.Select(t => t.Name).ToArray(),
                });
            }
            else if (active.Count == 0)
            {
                CliOutput.Info("No active tunnels.");
            }
            else
            {
                CliOutput.Info($"Active: {active.Count}");
                foreach (var t in active)
                    CliOutput.Info($"  • {t.Name}");
            }
            return 0;
        }

        // ── connect ───────────────────────────────────────────────────────────

        private static int CmdConnect(string[] args, AppConfig cfg, bool json, bool quiet)
        {
            bool useDefault = args.Any(a =>
                string.Equals(a, "--default", StringComparison.OrdinalIgnoreCase));

            StoredTunnel? tunnel;

            if (useDefault)
            {
                if (string.IsNullOrEmpty(cfg.DefaultTunnel))
                {
                    PrintResult(quiet, json, "error", "No default tunnel is configured.");
                    return 1;
                }
                tunnel = FindTunnel(cfg, cfg.DefaultTunnel);
                if (tunnel == null)
                {
                    PrintResult(quiet, json, "error",
                        $"Default tunnel '{cfg.DefaultTunnel}' not found in config.");
                    return 1;
                }
            }
            else
            {
                var name = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
                if (name == null)
                {
                    CliOutput.Error("Usage: MasselGUARD connect <name> [--json] [--quiet]");
                    CliOutput.Error("       MasselGUARD connect --default");
                    return 1;
                }
                tunnel = FindTunnel(cfg, name);
                if (tunnel == null)
                {
                    PrintResult(quiet, json, "error", $"Tunnel '{name}' not found.");
                    return 1;
                }
            }

            if (IsActive(tunnel))
            {
                PrintResult(quiet, json, "already_connected",
                    $"Tunnel '{tunnel.Name}' is already connected.");
                return 2;
            }

            bool ok = MakeTunnelService().Connect(tunnel, cfg, "CLI");
            if (ok)
                PrintResult(quiet, json, "connected", $"Tunnel '{tunnel.Name}' connected.");
            else
                PrintResult(quiet, json, "error", $"Failed to connect tunnel '{tunnel.Name}'.");

            return ok ? 0 : 1;
        }

        // ── disconnect ────────────────────────────────────────────────────────

        private static int CmdDisconnect(string[] args, AppConfig cfg, bool json, bool quiet)
        {
            var name = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
            if (name == null)
            {
                CliOutput.Error("Usage: MasselGUARD disconnect <name> [--json] [--quiet]");
                return 1;
            }

            var tunnel = FindTunnel(cfg, name);
            if (tunnel == null)
            {
                PrintResult(quiet, json, "error", $"Tunnel '{name}' not found.");
                return 1;
            }

            if (!IsActive(tunnel))
            {
                PrintResult(quiet, json, "already_disconnected",
                    $"Tunnel '{tunnel.Name}' is already disconnected.");
                return 2;
            }

            MakeTunnelService().Disconnect(tunnel);
            PrintResult(quiet, json, "disconnected", $"Tunnel '{tunnel.Name}' disconnected.");
            return 0;
        }

        // ── disconnect-all ────────────────────────────────────────────────────

        private static int CmdDisconnectAll(AppConfig cfg, bool json, bool quiet)
        {
            var active = cfg.Tunnels.Where(t => IsActive(t)).ToList();

            if (active.Count == 0)
            {
                PrintResult(quiet, json, "already_disconnected", "No active tunnels.");
                return 0;
            }

            var svc = MakeTunnelService();
            foreach (var t in active)
                svc.Disconnect(t);

            PrintResult(quiet, json, "disconnected",
                $"Disconnected {active.Count} tunnel(s).");
            return 0;
        }

        // ── version ───────────────────────────────────────────────────────────

        private static int CmdVersion(AppConfig cfg, bool json)
        {
            var ver      = UpdateChecker.CurrentVersionString;
            var stamp    = UpdateChecker.BuildStamp;
            var codename = UpdateChecker.Codename;

            // Determine update status from the cached last-check result in config.
            var    latestKnown = cfg.LatestKnownVersion;
            string updateStatus;
            if (string.IsNullOrEmpty(latestKnown))
                updateStatus = "unknown — run 'MasselGUARD status' to check";
            else if (UpdateChecker.IsNewerVersion(latestKnown))
                updateStatus = $"update available — v{latestKnown}";
            else if (UpdateChecker.IsAheadOfLatest(latestKnown))
                updateStatus = $"ahead of latest ({latestKnown})";
            else
                updateStatus = $"up to date";

            if (json)
            {
                CliOutput.PrintJson(new
                {
                    version       = ver,
                    codename      = string.IsNullOrEmpty(codename) ? null : (string?)codename,
                    build         = string.IsNullOrEmpty(stamp)    ? null : (string?)stamp,
                    update_status = updateStatus,
                });
            }
            else
            {
                CliOutput.Info(string.IsNullOrEmpty(codename)
                    ? $"MasselGUARD v{ver}"
                    : $"MasselGUARD v{ver}  |  {codename}");
                if (!string.IsNullOrEmpty(stamp))
                    CliOutput.Info($"build:   {stamp}");
                CliOutput.Info($"Harold Masselink  |  https://masselink.net");
                CliOutput.Info($"Update:  {updateStatus}");
            }
            return 0;
        }

        // ── help ──────────────────────────────────────────────────────────────

        private static int CmdHelp()
        {
            CliOutput.Info("MasselGUARD command-line interface");
            CliOutput.Info("");
            CliOutput.Info("Usage:");
            CliOutput.Info("  MasselGUARD <command> [options]");
            CliOutput.Info("");
            CliOutput.Info("Commands:");
            CliOutput.Info("  list,    --list       List all tunnels and their status");
            CliOutput.Info("  status,  --status     Show active tunnel count and names");
            CliOutput.Info("  connect <name>        Connect a tunnel by name");
            CliOutput.Info("  connect --default     Connect the configured default tunnel");
            CliOutput.Info("  disconnect <name>     Disconnect a tunnel by name");
            CliOutput.Info("  disconnect-all        Disconnect all active tunnels");
            CliOutput.Info("  version, --version    Show version, build, author and update status");
            CliOutput.Info("  help,    --help       Show this help");
            CliOutput.Info("");
            CliOutput.Info("Options (any command):");
            CliOutput.Info("  --json                Output in JSON format");
            CliOutput.Info("  --quiet, -q           No output — exit code only");
            CliOutput.Info("");
            CliOutput.Info("Exit codes:");
            CliOutput.Info("  0   Success");
            CliOutput.Info("  1   Error (tunnel not found, connect failed, etc.)");
            CliOutput.Info("  2   Already in desired state");
            CliOutput.Info("");
            CliOutput.Info("Notes:");
            CliOutput.Info("  Requires Administrator privileges.");
            CliOutput.Info("  Coexists safely with the GUI — changes appear within ~1 second.");
            CliOutput.Info("  connect / disconnect may take 1-3 s while the WireGuard service starts/stops.");
            return 0;
        }

        // ── unknown command ───────────────────────────────────────────────────

        private static int UnknownCmd(string cmd)
        {
            CliOutput.Error($"Unknown command: '{cmd}'. Run 'MasselGUARD help' for a list of commands.");
            return 1;
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Checks whether a tunnel is currently active.
        ///
        /// Local (wireguard-NT) tunnels: delegates to TunnelDll.IsRunning(), which
        /// probes the WireGuard management pipe — a single kernel call (~µs).
        ///
        /// WireGuard-for-Windows tunnels: checks the Windows service status.
        /// </summary>
        private static bool IsActive(StoredTunnel tunnel)
        {
            if (string.Equals(tunnel.Source, "local", StringComparison.OrdinalIgnoreCase))
                return TunnelDll.IsRunning(tunnel.Name);

            try
            {
                using var sc = new ServiceController("WireGuardTunnel$" + tunnel.Name);
                return sc.Status == ServiceControllerStatus.Running;
            }
            catch { return false; }
        }

        private static StoredTunnel? FindTunnel(AppConfig cfg, string name)
            => cfg.Tunnels.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        private static TunnelService MakeTunnelService()
            => new TunnelService(new LogService(), new ScriptService(), new HistoryService(), null);

        private static void PrintResult(bool quiet, bool json, string result, string message)
        {
            if (quiet) return;

            if (json)
                CliOutput.PrintJson(new { result, message });
            else if (result == "error")
                CliOutput.Error(message);
            else
                CliOutput.Ok(message);
        }
    }
}
