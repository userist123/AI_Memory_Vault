using LogAnalyzer.Dfir.Windows.Containment;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Lab S4 (2026-11-07): cmd.exe copied as C:\Users\Public\Libraries\upd\updsvc.exe was not listed by „Caută procese suspecte”.</summary>
public sealed class RenamedWindowsProgramTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-renamed-" + Guid.NewGuid().ToString("N"));

    public RenamedWindowsProgramTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_copy_of_cmd_under_another_name_is_recognised_and_the_real_one_is_not()
    {
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Assert.True(File.Exists(cmd), "cmd.exe is not on this machine");
        var copy = Path.Combine(_dir, "updsvc.exe");
        File.Copy(cmd, copy);
        Assert.Equal("cmd.exe", SuspiciousProcessDetector.RenamedWindowsProgram(copy));
        Assert.Null(SuspiciousProcessDetector.RenamedWindowsProgram(cmd));
        var mine = Path.Combine(_dir, "tool.exe");
        File.WriteAllBytes(mine, [0x4D, 0x5A]);
        Assert.Null(SuspiciousProcessDetector.RenamedWindowsProgram(mine));   // no version resource: not judged here
    }
}
