using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using MasselGUARD.Models;

namespace MasselGUARD.Services
{
    /// <summary>
    /// Handles all AppConfig persistence: load, save, import, export.
    /// No UI references. Raises ConfigChanged when the config is replaced.
    /// </summary>
    public class ConfigService
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasselGUARD", "config.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public AppConfig Config { get; private set; } = new();

        public event Action? ConfigChanged;

        /// <summary>
        /// True if no config.json existed when Load() was called
        /// (first run — show wizard).
        /// </summary>
        public bool IsFirstRun { get; private set; }

        // ── Load ─────────────────────────────────────────────────────────────
        public void Load()
        {
            if (!File.Exists(ConfigPath))
            {
                IsFirstRun = true;
                Config     = new AppConfig();
                return;
            }
            try
            {
                Config = JsonSerializer.Deserialize<AppConfig>(
                    File.ReadAllText(ConfigPath), JsonOpts) ?? new AppConfig();
            }
            catch
            {
                Config = new AppConfig();
            }

            MigrateInlineConfigsToFiles();
            MigrateProfileSources();
            MigrateNetworkLock();
        }

        private void MigrateNetworkLock()
        {
            var nl = Config.NetworkLock;
            if (nl == null)
            {
                Config.NetworkLock = new NetworkLockConfig();
                return;
            }

            bool dirty = false;
            if (nl.Enabled == true && nl.Mode == NetworkLockMode.Disabled)
            {
                nl.Mode = NetworkLockMode.Auto;
                dirty = true;
            }
            nl.Enabled = null;

            if (nl.LanExceptions == null!)
            {
                nl.LanExceptions = new List<string>();
                dirty = true;
            }
            if (nl.DnsExceptions == null!)
            {
                nl.DnsExceptions = new List<string>();
                dirty = true;
            }
            if (string.IsNullOrWhiteSpace(nl.DnsPolicy))
            {
                nl.DnsPolicy = "strict";
                dirty = true;
            }

            // Legacy global kill-switch mode "always" maps to Auto (per-tunnel triggers still apply).
            if (string.Equals(Config.KillSwitchMode, "always", StringComparison.OrdinalIgnoreCase)
                && nl.Mode == NetworkLockMode.Disabled)
            {
                nl.Mode = NetworkLockMode.Auto;
                dirty = true;
            }

            if (dirty) Save();
        }

        /// <summary>
        /// Ensures ProfileSource is set for legacy tunnels loaded without the field.
        /// </summary>
        private void MigrateProfileSources()
        {
            bool dirty = false;
            foreach (var t in Config.Tunnels)
            {
                // Default enum value Local is fine for local; re-derive when Source implies companion/managed.
                var expected = ProfileSourceExtensions.FromLegacySource(t.Source, t.ImportedAt);
                if (t.ProfileSource == ProfileSource.Local && expected != ProfileSource.Local)
                {
                    t.ProfileSource = expected;
                    dirty = true;
                }
                else if (t.ProfileSource == default && expected != ProfileSource.Local)
                {
                    t.ProfileSource = expected;
                    dirty = true;
                }
                if (t.Tags == null!)
                {
                    t.Tags = new List<string>();
                    dirty = true;
                }
            }
            if (dirty) Save();
        }

        /// <summary>
        /// One-time migration: move any inline Config blobs to .conf.dpapi files
        /// and clear the Config field so it no longer appears in config.json.
        /// </summary>
        private void MigrateInlineConfigsToFiles()
        {
            bool dirty = false;
            foreach (var t in Config.Tunnels)
            {
                if (string.IsNullOrEmpty(t.Config)) continue;

                // Decrypt the inline blob (DPAPI or legacy plaintext fallback).
                string plaintext;
                try
                {
                    var decrypted = ProtectedData.Unprotect(
                        Convert.FromBase64String(t.Config), null,
                        DataProtectionScope.CurrentUser);
                    plaintext = System.Text.Encoding.UTF8.GetString(decrypted);
                }
                catch
                {
                    plaintext = t.Config; // legacy plaintext stored by old GUI
                }

                // Only write a file when the existing path is missing or gone.
                if (string.IsNullOrEmpty(t.Path) || !File.Exists(t.Path))
                    t.Path = TunnelService.SaveConfigToFile(t.Name, plaintext);

                t.Config = null;
                dirty = true;
            }
            if (dirty) Save();
        }

        // ── Save ─────────────────────────────────────────────────────────────
        public void Save()
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);
            EnsureDirectoryAcl(dir);
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(Config, JsonOpts));
            ConfigChanged?.Invoke();
        }

        // ── Directory ACL ─────────────────────────────────────────────────────

        // Applied once per process run; also picks up existing installations
        // that were created before this hardening was added.
        private static bool _aclApplied;

        /// <summary>
        /// Restricts <paramref name="dir"/> (and everything inside it via
        /// inheritable rules) to the current user only — removes the default
        /// Administrators read-access inherited from %APPDATA%.
        /// Safe to call on an already-restricted directory.
        /// </summary>
        private static void EnsureDirectoryAcl(string dir)
        {
            if (_aclApplied) return;
            _aclApplied = true;
            try
            {
                var userSid  = WindowsIdentity.GetCurrent().User!;
                var security = new DirectorySecurity();

                // Block inheritance from %APPDATA% so our explicit rules are the only ones.
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                // Current user: full control, propagates into all subfolders and files.
                security.AddAccessRule(new FileSystemAccessRule(
                    userSid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                new DirectoryInfo(dir).SetAccessControl(security);
            }
            catch
            {
                // Non-fatal — ACL tightening is best-effort.
                // Failure here does not affect functionality.
            }
        }

        // ── Export ───────────────────────────────────────────────────────────
        /// <summary>
        /// Writes a .masselguard export file containing automation settings,
        /// rules, groups, and UI preferences. Never includes tunnel configs
        /// or DPAPI material.
        /// </summary>
        public void Export(string path, string appVersion)
        {
            var cfg = Config;
            var export = new
            {
                ExportVersion    = 1,
                AppVersion       = appVersion,
                ExportedAt       = DateTime.UtcNow,
                Rules            = cfg.Rules,
                TunnelGroups     = cfg.TunnelGroups,
                DefaultAction    = cfg.DefaultAction,
                DefaultTunnel    = cfg.DefaultTunnel,
                OpenWifiTunnel   = cfg.OpenWifiTunnel,
                ManualMode       = cfg.ManualMode,
                Mode             = cfg.Mode.ToString(),
                Language         = cfg.Language,
                ActiveTheme      = cfg.ActiveTheme,
                LogLevelSetting  = cfg.LogLevelSetting,
                ShowTrayPopupOnSwitch = cfg.ShowTrayPopupOnSwitch,
            };
            File.WriteAllText(path,
                JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
        }

        // ── Import ───────────────────────────────────────────────────────────
        /// <summary>
        /// Reads a .masselguard file and merges compatible fields into Config.
        /// Returns the AppVersion string found in the file (empty if absent).
        /// Unknown fields are silently ignored for forward compatibility.
        /// </summary>
        public string Import(string path)
        {
            var json = File.ReadAllText(path);
            using var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var fileVersion = root.TryGetProperty("AppVersion", out var v)
                ? v.GetString() ?? "" : "";

            void Str(string key, Action<string> set)
            {
                if (root.TryGetProperty(key, out var el) &&
                    el.ValueKind == JsonValueKind.String)
                    set(el.GetString() ?? "");
            }
            void Bool(string key, Action<bool> set)
            {
                if (root.TryGetProperty(key, out var el) &&
                    (el.ValueKind == JsonValueKind.True ||
                     el.ValueKind == JsonValueKind.False))
                    set(el.GetBoolean());
            }

            Str ("DefaultAction",     v => Config.DefaultAction    = v);
            Str ("DefaultTunnel",     v => Config.DefaultTunnel    = v);
            Str ("OpenWifiTunnel",    v => Config.OpenWifiTunnel   = v);
            Str ("Language",          v => Config.Language         = v);
            Str ("ActiveTheme",       v => Config.ActiveTheme      = v);
            Str ("LogLevelSetting",   v => Config.LogLevelSetting  = v);
            Bool("ManualMode",        v => Config.ManualMode       = v);
            Bool("ShowTrayPopupOnSwitch", v => Config.ShowTrayPopupOnSwitch = v);

            if (root.TryGetProperty("Rules", out var rulesEl))
            {
                var rules = JsonSerializer.Deserialize<
                    System.Collections.Generic.List<TunnelRule>>(
                    rulesEl.GetRawText(), opts);
                if (rules != null) Config.Rules = rules;
            }
            if (root.TryGetProperty("TunnelGroups", out var grpEl))
            {
                var groups = JsonSerializer.Deserialize<
                    System.Collections.Generic.List<TunnelGroup>>(
                    grpEl.GetRawText(), opts);
                if (groups != null) Config.TunnelGroups = groups;
            }

            Save();
            return fileVersion;
        }
    }
}
