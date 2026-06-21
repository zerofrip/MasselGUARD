using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasselGUARD.Models;

namespace MasselGUARD.Release
{
    /// <summary>Manifest-driven multi-component update (Phase 11).</summary>
    public sealed class UnifiedUpdateService
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

        private readonly string _installDir;
        private readonly string _channel;
        private readonly string _manifestBaseUrl;

        public UnifiedUpdateService(AppConfig cfg)
        {
            _installDir = ResolveInstallDir();
            _channel = string.IsNullOrWhiteSpace(cfg.UpdateChannel) ? "beta" : cfg.UpdateChannel.Trim();
            _manifestBaseUrl = string.IsNullOrWhiteSpace(cfg.UpdateManifestUrl)
                ? "https://releases.masselguard.net"
                : cfg.UpdateManifestUrl.TrimEnd('/');
        }

        public async Task<ReleaseManifest?> FetchManifestAsync(CancellationToken ct = default)
        {
            var url = $"{_manifestBaseUrl}/{_channel}/manifest.json";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ReleaseManifest>(json, JsonOpts);
        }

        public bool VerifyManifest(ReleaseManifest manifest, byte[]? publicKeyEd25519 = null)
        {
            if (manifest == null) return false;
            var key = publicKeyEd25519 ?? ReleaseSigningKeys.GetManifestPublicKey();
            if (key == null || key.Length == 0)
                return !ReleaseSigningKeys.RequireSignedManifest(_channel);
            if (string.IsNullOrEmpty(manifest.ManifestSignature)) return false;
            var payload = CanonicalManifestBytes(manifest);
            try
            {
                var sig = Convert.FromBase64String(manifest.ManifestSignature);
                return VerifyEd25519(sig, payload, key);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Verify manifest using channel policy and embedded/env public key.</summary>
        public bool VerifyManifestForChannel(ReleaseManifest manifest) =>
            VerifyManifest(manifest, ReleaseSigningKeys.GetManifestPublicKey());

        public async Task<UpdateApplyResult> ApplyAsync(
            ReleaseManifest manifest,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var staging = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MasselGUARD", "updates", "staging", manifest.ProductVersion);
            var backup = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MasselGUARD", "updates", "backup", manifest.ProductVersion);

            Directory.CreateDirectory(staging);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            CopyTree(_installDir, backup);

            progress?.Report("Stopping services…");
            StopServices(manifest);

            try
            {
                foreach (var comp in manifest.Components)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"Updating {comp.Id}…");
                    var dest = Path.Combine(_installDir, comp.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    if (!string.IsNullOrEmpty(comp.Url))
                    {
                        var tmp = Path.Combine(staging, comp.Id + ".bin");
                        await DownloadAsync(comp.Url, tmp, ct).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(comp.Sha256))
                            VerifySha256(tmp, comp.Sha256);
                        File.Copy(tmp, dest, overwrite: true);
                    }
                }

                progress?.Report("Restarting services…");
                StartServices(manifest);
                return new UpdateApplyResult { Ok = true, Version = manifest.ProductVersion };
            }
            catch (Exception ex)
            {
                progress?.Report("Rolling back…");
                CopyTree(backup, _installDir);
                StartServices(manifest);
                return new UpdateApplyResult { Ok = false, Error = ex.Message };
            }
        }

        private static void StopServices(ReleaseManifest manifest)
        {
            foreach (var svc in manifest.Components.Select(c => c.Service).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                RunSc($"stop \"{svc}\"");
        }

        private static void StartServices(ReleaseManifest manifest)
        {
            foreach (var svc in manifest.Components.Select(c => c.Service).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                RunSc($"start \"{svc}\"");
        }

        private static void RunSc(string args)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                p?.WaitForExit(15000);
            }
            catch { /* best effort */ }
        }

        private static async Task DownloadAsync(string url, string dest, CancellationToken ct)
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(dest);
            await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }

        private static void VerifySha256(string path, string expectedHex)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            var hash = sha.ComputeHash(fs);
            var hex = Convert.ToHexString(hash);
            if (!hex.Equals(expectedHex, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SHA256 mismatch for {path}");
        }

        private static void CopyTree(string src, string dest)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dest);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(src, dest));
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var target = file.Replace(src, dest);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }

        private static string ResolveInstallDir()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\MasselGUARD");
                var path = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path.TrimEnd('\\');
            }
            catch { /* ignore */ }

            return AppContext.BaseDirectory.TrimEnd('\\');
        }

        private static byte[] CanonicalManifestBytes(ReleaseManifest m)
        {
            var clone = new ReleaseManifest
            {
                SchemaVersion = m.SchemaVersion,
                Channel = m.Channel,
                ProductVersion = m.ProductVersion,
                ReleaseDate = m.ReleaseDate,
                Mandatory = m.Mandatory,
                MinSupportedVersion = m.MinSupportedVersion,
                Components = m.Components,
                ManifestSignature = null,
            };
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clone, JsonOpts));
        }

        private static bool VerifyEd25519(byte[] signature, byte[] message, byte[] publicKey)
        {
            if (signature.Length != 64 || publicKey.Length != 32) return false;
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(message, 0, message.Length);
            return verifier.VerifySignature(signature);
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
    }

    public sealed class ReleaseManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public string Channel { get; set; } = "beta";
        public string ProductVersion { get; set; } = "";
        public string? ReleaseDate { get; set; }
        public bool Mandatory { get; set; }
        public string? MinSupportedVersion { get; set; }
        public List<ReleaseComponent> Components { get; set; } = new();
        public string? ManifestSignature { get; set; }
    }

    public sealed class ReleaseComponent
    {
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string Path { get; set; } = "";
        public string? Sha256 { get; set; }
        public string? Url { get; set; }
        public string? DeltaUrl { get; set; }
        public string? AuthenticodeThumbprint { get; set; }
        public string? Service { get; set; }
        public bool StopBeforeUpdate { get; set; } = true;
        public bool RequiresReboot { get; set; }
    }

    public sealed class UpdateApplyResult
    {
        public bool Ok { get; set; }
        public string? Version { get; set; }
        public string? Error { get; set; }
    }
}
