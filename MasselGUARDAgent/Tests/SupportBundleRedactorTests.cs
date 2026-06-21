using MasselGUARD.Agent.Release;
using Xunit;

namespace MasselGUARD.Agent.Tests;

public class SupportBundleRedactorTests
{
    private const string Fixture = """
        {
          "endpoint": "203.0.113.1:51820",
          "remoteIp": "203.0.113.1",
          "publicIp": "198.51.100.42",
          "appPath": "C:\\Program Files\\Example\\app.exe",
          "machineName": "DESKTOP-ABC123",
          "ssid": "HomeWiFi",
          "privateKey": "abc123secret",
          "nested": { "endpointIp": "10.0.0.5" }
        }
        """;

    [Fact]
    public void Sanitized_redacts_endpoints_and_keys()
    {
        var redacted = SupportBundleRedactor.RedactJson(Fixture, "sanitized");
        Assert.Contains("<redacted>", redacted);
        Assert.DoesNotContain("203.0.113.1", redacted);
        Assert.DoesNotContain("abc123secret", redacted);
        Assert.Contains("sha256:", redacted);
        Assert.Contains("app.exe", redacted);
    }

    [Fact]
    public void Support_keeps_endpoints()
    {
        var redacted = SupportBundleRedactor.RedactJson(Fixture, "support");
        Assert.Contains("203.0.113.1", redacted);
        Assert.DoesNotContain("abc123secret", redacted);
    }

    [Fact]
    public void Crash_detail_sanitized_hashes()
    {
        var detail = "System.Exception: fail\n   at Foo.Bar() in C:\\src\\Foo.cs:line 10";
        var outSan = SupportBundleRedactor.RedactCrashDetail(detail, "sanitized");
        Assert.StartsWith("sha256:", outSan);
        Assert.Contains("at Foo.Bar()", outSan);
    }

    [Fact]
    public void Redaction_notes_vary_by_tier()
    {
        var san = SupportBundleRedactor.RedactionNotes("sanitized");
        var sup = SupportBundleRedactor.RedactionNotes("support");
        Assert.Contains("endpoints removed", san);
        Assert.Contains("endpoints retained", sup);
    }
}
