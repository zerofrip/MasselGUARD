using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MasselGUARD.Agent.Events;
using MasselGUARD.Cli;
using MasselGUARD.Models;
using MasselGUARD.Services;

namespace MasselGUARD.Agent.Services
{
    public sealed class TunnelListFilter
    {
        public string? Group { get; set; }
        public bool ActiveOnly { get; set; }
        public string? Search { get; set; }
        public bool IncludeArchived { get; set; }
        public string Sort { get; set; } = "name";
    }

    public sealed class TunnelImportOptions
    {
        public string? Path { get; set; }
        public string? Config { get; set; }
        public string? Name { get; set; }
        public string? Group { get; set; }
        public string OnConflict { get; set; } = "fail";
    }

    public sealed class TunnelProfileService
    {
        private readonly ConfigService _config;
        private readonly TunnelService _tunnels;
        private readonly Orchestrator _orch;
        private readonly AgentEventBus _bus;

        public TunnelProfileService(
            ConfigService config,
            TunnelService tunnels,
            Orchestrator orch,
            AgentEventBus bus)
        {
            _config  = config;
            _tunnels = tunnels;
            _orch    = orch;
            _bus     = bus;
        }

        public IEnumerable<object> List(TunnelListFilter filter)
        {
            var q = _orch.VisibleTunnels().AsEnumerable();

            if (!filter.IncludeArchived)
                q = q.Where(t => !t.Archived);

            if (!string.IsNullOrEmpty(filter.Group))
                q = q.Where(t => string.Equals(t.Group, filter.Group, StringComparison.OrdinalIgnoreCase));

            if (filter.ActiveOnly)
                q = q.Where(t => _tunnels.IsActive(t));

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim();
                q = q.Where(t =>
                    t.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                    || t.Group.Contains(s, StringComparison.OrdinalIgnoreCase)
                    || t.Notes.Contains(s, StringComparison.OrdinalIgnoreCase)
                    || (t.EndpointSummary?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (t.Tags?.Any(tag => tag.Contains(s, StringComparison.OrdinalIgnoreCase)) ?? false));
            }

            q = filter.Sort?.ToLowerInvariant() switch
            {
                "lastused" => q.OrderByDescending(t => t.LastUsedAt ?? DateTime.MinValue),
                "connectioncount" => q.OrderByDescending(t => t.ConnectionCount),
                "favorite" => q.OrderByDescending(t => t.Favorite).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase),
                _ => q.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase),
            };

            return q.Select(ToSummary).ToList();
        }

        public object Get(string name, bool includeConfig = true)
        {
            var t = RequireTunnel(name);
            string? config = null;
            WireGuardProfile? profile = null;

            if (includeConfig && CanReadConfig(t))
            {
                config = SafeDecrypt(t);
                if (!string.IsNullOrEmpty(config))
                    profile = WireGuardConf.Parse(config);
            }

            return new { summary = ToSummary(t), config, profile };
        }

        public object Create(string name, string config, string? group = null, string? notes = null,
            List<string>? tags = null)
        {
            name = TunnelValidationService.SanitizeTunnelName(name);
            var validation = TunnelValidationService.ValidateDraft(
                name, config, _config.Config.Tunnels, decrypt: SafeDecrypt);
            if (!validation.Valid)
                throw new InvalidOperationException(FormatValidationErrors(validation));

            var stored = new StoredTunnel
            {
                Name            = name,
                Source          = "local",
                ProfileSource   = ProfileSource.Local,
                Path            = TunnelService.SaveConfigToFile(name, config),
                Group           = group ?? "",
                Notes           = notes ?? "",
                Tags            = tags ?? new List<string>(),
                EndpointSummary = WireGuardConf.ExtractPrimaryEndpoint(config),
            };

            _config.Config.Tunnels.Add(stored);
            _config.Save();
            PublishProfileEvent(AgentEventTypes.TunnelCreated, stored);
            return ToSummary(stored);
        }

        public object Update(string name, JsonElement patch)
        {
            var t = RequireTunnel(name);
            bool wasActive = _tunnels.IsActive(t);

            if (patch.TryGetProperty("newName", out var nn))
            {
                var newName = TunnelValidationService.SanitizeTunnelName(nn.GetString() ?? "");
                if (!string.IsNullOrEmpty(newName) && !string.Equals(newName, t.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var nameVal = TunnelValidationService.ValidateName(newName, _config.Config.Tunnels, t.Name);
                    if (!nameVal.Valid) throw new InvalidOperationException(FormatValidationErrors(nameVal));
                    t.Name = newName;
                }
            }

            if (patch.TryGetProperty("group", out var g)) t.Group = g.GetString() ?? "";
            if (patch.TryGetProperty("notes", out var no)) t.Notes = no.GetString() ?? "";
            if (patch.TryGetProperty("favorite", out var fav)) t.Favorite = fav.GetBoolean();
            if (patch.TryGetProperty("archived", out var ar)) t.Archived = ar.GetBoolean();
            if (patch.TryGetProperty("killSwitch", out var ks)) t.KillSwitch = ks.GetBoolean();
            if (patch.TryGetProperty("autoReconnect", out var ac)) t.AutoReconnect = ac.GetBoolean();
            if (patch.TryGetProperty("tags", out var tagsEl))
                t.Tags = JsonSerializer.Deserialize<List<string>>(tagsEl.GetRawText()) ?? new List<string>();

            if (patch.TryGetProperty("config", out var c) && c.GetString() is string conf)
            {
                if (!IsConfigEditable(t, wasActive))
                    throw new InvalidOperationException(
                        "Configuration cannot be edited for this profile while active or when managed externally.");

                var validation = TunnelValidationService.ValidateDraft(
                    null, conf, _config.Config.Tunnels, t.Name, SafeDecrypt);
                if (!validation.Valid)
                    throw new InvalidOperationException(FormatValidationErrors(validation));

                t.Path = TunnelService.SaveConfigToFile(t.Name, conf);
                t.EndpointSummary = WireGuardConf.ExtractPrimaryEndpoint(conf);
            }

            _config.Save();
            PublishProfileEvent(AgentEventTypes.TunnelUpdated, t);
            return ToSummary(t);
        }

        public object Delete(string name)
        {
            var t = RequireTunnel(name);
            if (_tunnels.IsActive(t))
                _orch.DisconnectTunnel(t, "Delete");

            _config.Config.Tunnels.RemoveAll(x =>
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(t.Path) && File.Exists(t.Path))
                File.Delete(t.Path);

            _config.Save();
            _bus.Publish(AgentEventTypes.TunnelDeleted, new
            {
                name,
                profileSource = ProfileSourceExtensions.ToApiString(t.ProfileSource),
            });
            return new { ok = true, name };
        }

        public object Clone(string name, string? newName = null)
        {
            var src = RequireTunnel(name);
            if (!ProfileSourceExtensions.IsConfigEditable(src.ProfileSource))
                throw new InvalidOperationException("Companion and managed profiles cannot be cloned with config.");

            var plain = SafeDecrypt(src);
            if (string.IsNullOrEmpty(plain))
                throw new InvalidOperationException("Source tunnel has no readable configuration.");

            var cloneName = TunnelValidationService.SanitizeTunnelName(newName ?? $"{src.Name} (copy)");
            var nameVal = TunnelValidationService.ValidateName(cloneName, _config.Config.Tunnels);
            if (!nameVal.Valid)
            {
                cloneName = TunnelValidationService.SanitizeTunnelName(
                    $"{src.Name} ({DateTime.UtcNow:yyyyMMddHHmmss})");
                nameVal = TunnelValidationService.ValidateName(cloneName, _config.Config.Tunnels);
                if (!nameVal.Valid) throw new InvalidOperationException(FormatValidationErrors(nameVal));
            }

            var stored = new StoredTunnel
            {
                Name            = cloneName,
                Source          = "local",
                ProfileSource   = src.ProfileSource == ProfileSource.Imported
                    ? ProfileSource.Imported : ProfileSource.Local,
                Path            = TunnelService.SaveConfigToFile(cloneName, plain),
                Group           = src.Group,
                Notes           = src.Notes,
                Tags            = new List<string>(src.Tags ?? new List<string>()),
                KillSwitch      = src.KillSwitch,
                AutoReconnect   = src.AutoReconnect,
                EndpointSummary = src.EndpointSummary ?? WireGuardConf.ExtractPrimaryEndpoint(plain),
            };

            _config.Config.Tunnels.Add(stored);
            _config.Save();
            _bus.Publish(AgentEventTypes.TunnelCloned, new { summary = ToSummary(stored), sourceName = name });
            return ToSummary(stored);
        }

        public object Import(TunnelImportOptions opts)
        {
            string plain;
            string tunnelName;

            if (!string.IsNullOrEmpty(opts.Path))
            {
                plain = File.ReadAllText(opts.Path!);
                tunnelName = opts.Name ?? Path.GetFileNameWithoutExtension(opts.Path!);
            }
            else if (!string.IsNullOrEmpty(opts.Config))
            {
                plain = opts.Config!;
                tunnelName = opts.Name ?? "imported";
            }
            else throw new ArgumentException("path or config required");

            tunnelName = TunnelValidationService.SanitizeTunnelName(tunnelName);
            if (string.IsNullOrEmpty(tunnelName)) tunnelName = "imported";

            if (_orch.FindTunnel(tunnelName) != null)
            {
                if (string.Equals(opts.OnConflict, "rename", StringComparison.OrdinalIgnoreCase))
                {
                    var baseName = tunnelName;
                    int i = 2;
                    while (_orch.FindTunnel(tunnelName) != null)
                        tunnelName = $"{baseName} ({i++})";
                }
                else throw new InvalidOperationException($"Tunnel already exists: {tunnelName}");
            }

            var validation = TunnelValidationService.ValidateDraft(
                tunnelName, plain, _config.Config.Tunnels, decrypt: SafeDecrypt);
            if (!validation.Valid)
                throw new InvalidOperationException(FormatValidationErrors(validation));

            var stored = new StoredTunnel
            {
                Name            = tunnelName,
                Source          = "local",
                ProfileSource   = ProfileSource.Imported,
                ImportedAt      = DateTime.UtcNow,
                Path            = TunnelService.SaveConfigToFile(tunnelName, plain),
                Group           = opts.Group ?? "",
                EndpointSummary = WireGuardConf.ExtractPrimaryEndpoint(plain),
            };

            _config.Config.Tunnels.Add(stored);
            _config.Save();
            PublishProfileEvent(AgentEventTypes.TunnelImported, stored);
            return ToSummary(stored);
        }

        public object Export(string name, string mode = "full", string? dest = null)
        {
            var t = RequireTunnel(name);
            var plain = SafeDecrypt(t) ?? "";
            if (string.IsNullOrEmpty(plain))
                throw new InvalidOperationException("No configuration available for this profile.");

            string output = mode.ToLowerInvariant() switch
            {
                "sanitized" => WireGuardConf.Sanitize(plain),
                "qr"        => WireGuardConf.ToQrPayload(plain),
                _           => plain,
            };

            if (!string.IsNullOrEmpty(dest))
                File.WriteAllText(dest!, output);

            return new { name, config = output, mode, written = dest };
        }

        public ValidationResult Validate(string? name, string? config, string? excludeName = null) =>
            TunnelValidationService.ValidateDraft(
                name, config, _config.Config.Tunnels, excludeName, SafeDecrypt);

        public void RecordUsageOnConnect(StoredTunnel t, string? endpoint)
        {
            t.LastUsedAt = DateTime.UtcNow;
            t.ConnectionCount++;
            if (!string.IsNullOrEmpty(endpoint))
                t.EndpointSummary = endpoint;
            _config.Save();
        }

        public object ToSummary(StoredTunnel t)
        {
            bool active = _tunnels.IsActive(t);
            bool configEditable = ProfileSourceExtensions.IsConfigEditable(t.ProfileSource)
                                  && !active
                                  && t.ProfileSource != ProfileSource.Managed;

            return new
            {
                name = t.Name,
                group = t.Group,
                source = t.Source,
                notes = t.Notes,
                active,
                killSwitch = t.KillSwitch,
                autoReconnect = t.AutoReconnect,
                profileSource = ProfileSourceExtensions.ToApiString(t.ProfileSource),
                favorite = t.Favorite,
                tags = t.Tags ?? new List<string>(),
                archived = t.Archived,
                lastUsedAt = t.LastUsedAt?.ToString("o"),
                connectionCount = t.ConnectionCount,
                endpointSummary = t.EndpointSummary,
                configEditable,
            };
        }

        private StoredTunnel RequireTunnel(string name) =>
            _orch.FindTunnel(name) ?? throw new InvalidOperationException($"Tunnel not found: {name}");

        private static bool CanReadConfig(StoredTunnel t) =>
            !string.IsNullOrEmpty(t.Path) || !string.IsNullOrEmpty(t.Config);

        private static bool IsConfigEditable(StoredTunnel t, bool active) =>
            ProfileSourceExtensions.IsConfigEditable(t.ProfileSource) && !active;

        private string? SafeDecrypt(StoredTunnel t)
        {
            try { return TunnelService.DecryptConfig(t); }
            catch { return null; }
        }

        private void PublishProfileEvent(string eventType, StoredTunnel t) =>
            _bus.Publish(eventType, ToSummary(t));

        private static string FormatValidationErrors(ValidationResult r) =>
            string.Join("; ", r.Errors.Select(e => e.Message));
    }
}
