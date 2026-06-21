using System;
using System.Text;
using System.Text.Json;
using MasselGUARD.Agent.Release;
using SharpFuzz;

namespace MasselGUARD.Fuzz.SupportRedactor;

internal static class Program
{
    private static readonly string[] Tiers = ["sanitized", "support", "full"];

    public static void Main(string[] args)
    {
        Fuzzer.Run(stream =>
        {
            var len = (int)Math.Min(stream.Length, 64 * 1024);
            var buf = new byte[len];
            stream.ReadExactly(buf, 0, len);
            var json = Encoding.UTF8.GetString(buf);
            var tier = Tiers[len % Tiers.Length];
            try
            {
                var redacted = SupportBundleRedactor.RedactJson(json, tier);
                if (redacted.Contains("abc123secret", StringComparison.Ordinal))
                    throw new InvalidOperationException("secret leaked in redacted output");
            }
            catch (JsonException)
            {
                /* invalid JSON ok */
            }
        });
    }
}
