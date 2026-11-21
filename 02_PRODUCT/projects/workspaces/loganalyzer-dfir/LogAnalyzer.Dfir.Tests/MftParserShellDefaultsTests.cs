using LogAnalyzer.Dfir.FileSystem;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Lab WS01 (clean Windows 11, 2026-11-13): after the system-tree filter the only 10 mismatches left were the shell's desktop.ini /
/// RecordedTV.library-ms in \Users\Public and \Users\desktop.ini, and they made AF08 say "Detected" on a clean station. Exact names are
/// counted as defaults; every other file in \Users\Public stays examined.
/// </summary>
public sealed class MftParserShellDefaultsTests
{
    [Theory]
    [InlineData(@"\Users\desktop.ini")]
    [InlineData(@"\Users\Public\desktop.ini")]
    [InlineData(@"\Users\Public\Documents\desktop.ini")]
    [InlineData(@"\Users\Public\Libraries\RecordedTV.library-ms")]   // the one default the image provisions, exact path
    public void Shell_default_files_in_users_and_public_are_counted_not_examined(string path) =>
        Assert.Equal(MftParser.ShellDefaultsRoot, MftParser.SystemTreeOf(path));

    [Theory]
    [InlineData(@"\Users\Public\Libraries\Invoice.library-ms")]        // any other .library-ms (known NTLM-hash lure): examined
    [InlineData(@"\Users\Public\Documents\RecordedTV.library-ms")]
    [InlineData(@"\Windows\Temp\x.exe")]
    [InlineData(@"\Windows\Tasks\x.job")]
    [InlineData(@"\Windows\Tracing\x.dll")]
    [InlineData(@"\Windows\System32\spool\drivers\color\x.exe")]
    [InlineData(@"\Windows\System32\Tasks\Evil")]
    [InlineData(@"\$Recycle.Bin\S-1-5-21-1\$R1.exe")]
    public void User_writable_places_inside_system_trees_are_examined(string path) => Assert.Null(MftParser.SystemTreeOf(path));

    [Theory]
    [InlineData(@"\Users\Public\Documents\tool.exe")]
    [InlineData(@"\Users\Public\desktop.ini.exe")]
    [InlineData(@"\Users\Public\Downloads\payload.dll")]
    [InlineData(@"\Users\it.admin\Desktop\desktop.ini")]
    [InlineData(@"\Users\it.admin\Documents\notes.library-ms")]
    [InlineData(@"\Temp\desktop.ini")]
    public void Everything_else_stays_examined(string path) => Assert.Null(MftParser.SystemTreeOf(path));
}
