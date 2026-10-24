using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Finding B10: the .NET 10.0.13 warning (SignedCms fix) is shown at the start of every edition and tool, never blocks, never needs the network.</summary>
public sealed class RuntimeGuardEntryPointTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    [Theory]
    [InlineData("10.0.0", true)]
    [InlineData("10.0.12", true)]
    [InlineData("10.0.13", false)]
    [InlineData("10.0.14", false)]
    [InlineData("10.1.0", false)]
    public void The_guard_warns_only_below_10_0_13(string version, bool warns)
    {
        var w = RuntimeGuard.Warning(Version.Parse(version));
        Assert.Equal(warns, w is not null);
        if (warns) Assert.Contains("10.0.13", w);
    }

    [Fact]
    public void The_startup_notice_is_set_for_an_old_runtime_and_empty_for_a_current_one_and_never_throws()
    {
        try
        {
            Assert.StartsWith("Atenție .NET:", StartupNotices.Initialize(new Version(10, 0, 12)));
            Assert.True(StartupNotices.HasRuntimeWarning);
            Assert.Equal("", StartupNotices.Initialize(new Version(10, 0, 13)));
            Assert.False(StartupNotices.HasRuntimeWarning);
        }
        finally { StartupNotices.Initialize(); }
    }

    [Fact]
    public void Every_executable_entry_point_of_the_solution_calls_the_runtime_guard()
    {
        var root = Root();
        // the projects that build an executable (WinExe / Exe), except the tests; App.Network and App.Classified compile the sources of LogAnalyzer.App
        var exes = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("Tests", StringComparison.Ordinal) && Regex.IsMatch(File.ReadAllText(f), @"<OutputType>\s*(Win)?Exe\s*</OutputType>"))
            .Select(f => Path.GetFileNameWithoutExtension(f)).Where(n => Directory.Exists(Path.Combine(root, n))).ToList();
        Assert.Contains("LogAnalyzer.App", exes);
        Assert.Contains("LogAnalyzer.KeyGen", exes);
        Assert.Contains("LogAnalyzer.LicenseManager", exes);

        var entry = new Dictionary<string, (string File, string Needle)>
        {
            ["LogAnalyzer.App"] = (Path.Combine("LogAnalyzer.App", "App.xaml.cs"), "StartupNotices.Initialize("),
            ["LogAnalyzer.KeyGen"] = (Path.Combine("LogAnalyzer.KeyGen", "Program.cs"), "RuntimeGuard.Warning("),
            ["LogAnalyzer.LicenseManager"] = (Path.Combine("LogAnalyzer.LicenseManager", "MainWindow.xaml.cs"), "RuntimeGuard.Warning("),
        };
        foreach (var exe in exes)
        {
            if (exe is "LogAnalyzer.App.Network" or "LogAnalyzer.App.Classified")
            {
                // the same OnStartup as the unclassified application: the project compiles the sources of LogAnalyzer.App
                Assert.Contains(@"..\LogAnalyzer.App\**\*.cs", File.ReadAllText(Path.Combine(root, exe, exe + ".csproj")), StringComparison.Ordinal);
                continue;
            }
            Assert.True(entry.ContainsKey(exe), $"{exe} is an executable without a RuntimeGuard check: add it to this test and call RuntimeGuard at its start");
            var (file, needle) = entry[exe];
            Assert.Contains(needle, File.ReadAllText(Path.Combine(root, file)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_guard_cannot_block_or_reach_the_network()
    {
        var root = Root();
        var guard = File.ReadAllText(Path.Combine(root, "LogAnalyzer.Dfir.Core", "Reporting", "RuntimeGuard.cs"));
        var notices = File.ReadAllText(Path.Combine(root, "LogAnalyzer.App", "Services", "StartupNotices.cs"));
        foreach (var text in new[] { guard, notices })
        {
            Assert.DoesNotMatch(@"HttpClient|WebClient|Socket|System\.Net\.|MessageBox|\bthrow\b|Environment\.Exit|Shutdown\(", text);
        }
    }
}
