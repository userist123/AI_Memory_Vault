using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Regression for the first-run crash ("The calling thread cannot access this object because a different thread owns it"): SignInWindow read
/// AccountBox.Text inside Task.Run. WPF controls may only be read on their own thread, so no Task.Run lambda in the app may touch a control.
/// </summary>
public sealed class UiThreadAccessTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("LogAnalyzer.App not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void No_background_task_reads_a_control_of_a_window()
    {
        var offenders = new List<string>();
        foreach (var f in Directory.EnumerateFiles(AppDir(), "*.xaml.cs", SearchOption.AllDirectories))
        {
            var src = File.ReadAllText(f);
            // control names declared in the matching XAML (x:Name="…")
            var xaml = f[..^3];
            if (!File.Exists(xaml)) continue;
            var names = Regex.Matches(File.ReadAllText(xaml), @"x:Name=""([A-Za-z_][A-Za-z0-9_]*)""").Select(m => m.Groups[1].Value).ToHashSet();
            foreach (Match m in Regex.Matches(src, @"Task\.Run\(\s*\(\)\s*=>(?<body>[^;]*)"))
                foreach (var n in names)
                    if (Regex.IsMatch(m.Groups["body"].Value, $@"\b{Regex.Escape(n)}\."))
                        offenders.Add($"{Path.GetFileName(f)}: {n} inside Task.Run → {m.Value.Trim()}");
        }
        Assert.True(offenders.Count == 0, "Controls read on a background thread:\n" + string.Join("\n", offenders));
    }
}
