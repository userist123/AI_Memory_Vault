using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Windows.Acquisition;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Windows path semantics for evidence paths, identical on every host (PR212 research item 4).</summary>
public sealed class WinPathTests
{
    // Expected values are what System.IO.Path returns on Windows.
    [Theory]
    [InlineData(@"C:\Users\u\a.exe", "a.exe")]
    [InlineData(@"C:\Users\u\", "")]
    [InlineData("C:/Users/u/a.exe", "a.exe")]
    [InlineData(@"\\server\share\dir\f.txt", "f.txt")]
    [InlineData(@"\??\C:\ProgramData\csrss.exe", "csrss.exe")]
    [InlineData("a.exe", "a.exe")]
    [InlineData(@"C:", "")]
    [InlineData(@"C:file.txt", "file.txt")]
    [InlineData("", "")]
    public void GetFileName(string path, string expected) => Assert.Equal(expected, WinPath.GetFileName(path));

    [Theory]
    [InlineData(@"C:\Users\u\a.exe", @"C:\Users\u")]
    [InlineData(@"C:\a.exe", @"C:\")]
    [InlineData(@"C:\", null)]
    [InlineData(@"C:\Users\u\", @"C:\Users\u")]
    [InlineData("C:/Users/u/a.exe", "C:/Users/u")]
    [InlineData("a.exe", "")]
    [InlineData(@"\a.exe", @"\")]
    [InlineData(@"\\server\share\dir\f.txt", @"\\server\share\dir")]
    [InlineData(@"\\server\share\f.txt", @"\\server\share")]
    [InlineData(@"\\server\share", null)]
    [InlineData(@"\VOLUME{01d0}\WINDOWS\TEMP\SVCHOST.EXE", @"\VOLUME{01d0}\WINDOWS\TEMP")]
    [InlineData("", null)]
    public void GetDirectoryName(string path, string? expected) => Assert.Equal(expected, WinPath.GetDirectoryName(path));

    [Theory]
    [InlineData(@"C:\dl\TOOL_302044.zip", "TOOL_302044")]
    [InlineData(@"C:\dl\a.b.c", "a.b")]
    [InlineData(@"C:\dl\noext", "noext")]
    [InlineData(@"C:\dl\.hidden", "")]
    public void GetFileNameWithoutExtension(string path, string expected) => Assert.Equal(expected, WinPath.GetFileNameWithoutExtension(path));

    [Theory]
    [InlineData(@"C:\dl\a.exe", ".exe")]
    [InlineData(@"C:\dl\a.", "")]
    [InlineData(@"C:\dl.d\noext", "")]
    [InlineData(@"C:\dl\a.tar.gz", ".gz")]
    public void GetExtension(string path, string expected) => Assert.Equal(expected, WinPath.GetExtension(path));

    [Fact]
    public void Null_input_is_handled()
    {
        Assert.Equal("", WinPath.GetFileName(null));
        Assert.Null(WinPath.GetDirectoryName(null));
    }
}

/// <summary>A CaseId or operator name with line breaks must not be able to escape the '#' comment of the generated PowerShell package.</summary>
public sealed class PackageScriptSanitisationTests
{
    [Theory]
    [InlineData("a\r\nb", "a  b")]
    [InlineData("a\nb", "a b")]
    [InlineData("a\rb", "a b")]
    [InlineData("a\u0085b", "a b")]
    [InlineData("a\u2028b\u2029c", "a b c")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void OneLine_removes_every_line_terminator_PowerShell_recognises(string input, string expected)
        => Assert.Equal(expected, RemoteCollection.OneLine(input));

    [Fact]
    public void A_CaseId_with_CRLF_cannot_inject_a_command_line_into_the_package_script()
    {
        var req = new RemoteCollectionRequest(Guid.NewGuid().ToString(), "CASE-1\r\nRemove-Item -Recurse C:\\ ; #", "WS01", "op\nsecond", "why",
            ["evtx:System"], DateTimeOffset.UtcNow);
        var script = RemoteCollection.PackageScript(req, new string('A', 64));
        var header = script.Split('\n').First(l => l.StartsWith("# Cerere", StringComparison.Ordinal));
        Assert.Contains("CASE-1", header);
        Assert.Contains("Remove-Item", header);                              // kept as inert comment text on the same line
        Assert.DoesNotContain(script.Split('\n').Select(l => l.TrimEnd('\r')), l => l.StartsWith("Remove-Item", StringComparison.Ordinal));
        Assert.DoesNotContain(script.Split('\n').Select(l => l.TrimEnd('\r')), l => l.StartsWith("second", StringComparison.Ordinal));
    }
}
