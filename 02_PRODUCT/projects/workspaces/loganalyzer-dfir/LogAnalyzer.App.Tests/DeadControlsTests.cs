using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Owner 2026-10-11: „multe dintre opțiuni nu te lasă să faci nimic, sunt acolo doar să fie". Every button in every view does something (a Command
/// that exists in the code, a Click handler or a context menu), and no command body is a placeholder.
/// </summary>
public sealed class DeadControlsTests
{
    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
        throw new DirectoryNotFoundException("LogAnalyzer.App");
    }

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(AppDir(), pattern, SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Every_button_has_a_command_a_click_handler_or_a_menu()
    {
        var dead = new List<string>();
        foreach (var f in Files("*.xaml"))
            foreach (var b in XDocument.Load(f).Descendants().Where(e => e.Name.LocalName == "Button"))
                if (!b.Attributes().Any(a => a.Name.LocalName is "Command" or "Click") && !b.Elements().Any(e => e.Name.LocalName == "Button.ContextMenu"))
                    dead.Add($"{Path.GetFileName(f)}: {b.Attribute("Content")?.Value ?? b.ToString()[..Math.Min(80, b.ToString().Length)]}");
        Assert.True(dead.Count == 0, "buttons that do nothing:\n" + string.Join("\n", dead));
    }

    [Fact]
    public void Every_bound_command_exists_and_is_not_a_placeholder()
    {
        var code = string.Join("\n", Files("*.cs").Select(File.ReadAllText));
        var defined = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(code, @"\[RelayCommand[^\]]*\]\s*(?:private|public|internal|protected)?\s*(?:async\s+)?[\w<>\.?]+\s+(\w+)\s*\([^)]*\)\s*(=>[^;]*;|\{)"))
        {
            var name = m.Groups[1].Value;
            var cmd = (name.EndsWith("Async") ? name[..^5] : name) + "Command";
            var body = m.Groups[2].Value;
            if (body == "{")
            {
                int i = m.Index + m.Length, depth = 1;
                while (depth > 0 && i < code.Length) { depth += code[i] == '{' ? 1 : code[i] == '}' ? -1 : 0; i++; }
                body = code[(m.Index + m.Length)..(i - 1)];
            }
            defined[cmd] = body;
        }
        foreach (Match m in Regex.Matches(code, @"(?:ICommand|RelayCommand|AsyncRelayCommand)\S*\s+(\w+Command)\s*\{")) defined.TryAdd(m.Groups[1].Value, "<explicit>");

        var problems = new List<string>();
        foreach (var f in Files("*.xaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"Command=""\{Binding\s+(?:Path=)?([\w\.]+)"))
            {
                var name = m.Groups[1].Value.Split('.')[^1];
                if (!defined.TryGetValue(name, out var body)) problems.Add($"{Path.GetFileName(f)}: {name} does not exist");
                else if (Regex.IsMatch(body, @"NotImplemented|în curând|coming soon|\bTODO\b|neimplementat")) problems.Add($"{Path.GetFileName(f)}: {name} is a placeholder");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }
}
