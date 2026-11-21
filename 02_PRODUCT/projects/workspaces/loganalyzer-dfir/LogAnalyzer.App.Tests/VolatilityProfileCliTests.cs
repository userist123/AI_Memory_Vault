using System;
using System.IO;
using System.Security.Principal;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Master plan 6.6, KeyGen volatility-profile: the approval is an administrator action and never writes a profile it cannot protect.</summary>
public sealed class VolatilityProfileCliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-volcli-" + Guid.NewGuid().ToString("N"));

    public VolatilityProfileCliTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static bool Elevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [Fact]
    public void Wrong_arguments_print_the_usage_and_write_nothing()
    {
        var o = new StringWriter();
        Assert.Equal(1, ToolsCli.VolatilityProfile(["volatility-profile", "only-one"], o));
        Assert.Contains("Utilizare", o.ToString());
    }

    [Fact]
    public void Without_administrator_rights_or_with_a_tool_folder_others_can_change_no_profile_is_written()
    {
        var tool = Path.Combine(_dir, "tool"); Directory.CreateDirectory(tool);
        File.WriteAllBytes(Path.Combine(tool, "vol.exe"), [0x4D, 0x5A]);
        var symbols = Path.Combine(_dir, "symbols"); Directory.CreateDirectory(symbols);
        var profile = Path.Combine(_dir, "profile");
        var o = new StringWriter();
        int rc = ToolsCli.VolatilityProfile(["volatility-profile", Path.Combine(tool, "vol.exe"), symbols, "--profile-dir", profile], o);
        // not elevated: refused as such; elevated: the temporary tool folder belongs to the test account, so it is refused for its rights
        Assert.Equal(Elevated() ? 4 : 3, rc);
        Assert.False(Directory.Exists(profile));
        Assert.Contains(Elevated() ? "alte conturi" : "administrator", o.ToString());
    }
}
