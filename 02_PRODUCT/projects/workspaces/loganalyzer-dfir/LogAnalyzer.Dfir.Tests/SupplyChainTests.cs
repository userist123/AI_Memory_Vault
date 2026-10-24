using System.Text.RegularExpressions;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Security audit #9: lock files for every project, locked restore in CI / Release, no floating package versions.</summary>
public sealed class SupplyChainTests
{
    private static string Root()
    {
        var d = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(d, "LogAnalyzer.slnx"))) d = Path.GetDirectoryName(d)!;
        return d;
    }

    private static IEnumerable<string> SolutionProjects(string root) =>
        Regex.Matches(File.ReadAllText(Path.Combine(root, "LogAnalyzer.slnx")), "Path=\"([^\"]+\\.csproj)\"").Select(m => Path.Combine(root, m.Groups[1].Value));

    [Fact]
    public void Every_solution_project_has_a_committed_lock_file()
    {
        var root = Root();
        var projects = SolutionProjects(root).ToList();
        Assert.NotEmpty(projects);
        var missing = projects.Where(p => !File.Exists(Path.Combine(Path.GetDirectoryName(p)!, "packages.lock.json"))).Select(Path.GetFileName).ToList();
        Assert.True(missing.Count == 0, "missing packages.lock.json: " + string.Join(", ", missing));
    }

    [Fact]
    public void Restore_uses_lock_files_and_locked_mode_in_ci_and_release()
    {
        var props = File.ReadAllText(Path.Combine(Root(), "Directory.Build.props"));
        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", props);
        Assert.Matches("<RestoreLockedMode Condition=\"[^\"]*'\\$\\(Configuration\\)' == 'Release'[^\"]*\">true</RestoreLockedMode>", props);
        Assert.Contains("GITHUB_ACTIONS", props);
    }

    [Fact]
    public void No_package_version_floats()
    {
        var floating = SolutionProjects(Root())
            .SelectMany(p => Regex.Matches(File.ReadAllText(p), "PackageReference Include=\"([^\"]+)\" Version=\"([^\"]*\\*[^\"]*)\"").Select(m => $"{Path.GetFileName(p)}: {m.Groups[1].Value} {m.Groups[2].Value}"))
            .ToList();
        Assert.True(floating.Count == 0, "floating versions: " + string.Join("; ", floating));
    }
}
