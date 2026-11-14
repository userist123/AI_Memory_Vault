using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Lab 2026-11-10: the default per-user OneDrive task and Run value were reported as High / Medium persistence on every Windows 11 station.
/// Only the exact default executables in the default folder are recognised; the location is user-writable, so the finding stays (Low, check the signature).
/// </summary>
public class DefaultOneDrivePathTests
{
    [Theory]
    [InlineData(@"%localappdata%\Microsoft\OneDrive\OneDriveStandaloneUpdater.exe")]
    [InlineData(@"""C:\Users\ana\AppData\Local\Microsoft\OneDrive\OneDrive.exe"" /background")]
    [InlineData(@"C:\Users\ana.pop\AppData\Local\Microsoft\OneDrive\OneDrive.exe /background")]
    [InlineData(@"%LOCALAPPDATA%\MICROSOFT\ONEDRIVE\ONEDRIVE.EXE")]
    public void The_default_executables_in_the_default_folder_are_recognised(string command) => Assert.True(LiveStateAnalyzer.IsDefaultOneDrivePath(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"%localappdata%\Microsoft\OneDrive\updsvc.exe")]                                  // another name in the same folder
    [InlineData(@"%localappdata%\Microsoft\OneDrive\sub\OneDrive.exe")]                           // a sub-folder
    [InlineData(@"%localappdata%\Microsoft\OneDrive\..\evil\OneDrive.exe")]                       // traversal
    [InlineData(@"C:\Users\..\AppData\Local\Microsoft\OneDrive\OneDrive.exe")]                    // not a profile
    [InlineData(@"C:\Users\Public\OneDrive.exe")]                                                 // the name alone is not enough
    [InlineData(@"%appdata%\Microsoft\OneDrive\OneDrive.exe")]                                    // roaming, not the default
    [InlineData(@"%localappdata%\Microsoft\OneDrive\OneDrive.exe.bat")]                           // a longer name
    [InlineData(@"cmd.exe /c %localappdata%\Microsoft\OneDrive\OneDrive.exe")]                    // a wrapper
    [InlineData(@"\\server\share\Users\a\AppData\Local\Microsoft\OneDrive\OneDrive.exe")]         // a network path
    public void Anything_else_keeps_its_full_severity(string? command) => Assert.False(LiveStateAnalyzer.IsDefaultOneDrivePath(command));
}
