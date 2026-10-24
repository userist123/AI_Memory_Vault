using System.Text;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The user guide's annex of checks and codes (docs/utilizare/ANEXA_VERIFICARI_SI_CODURI.md) is generated from the same texts the application shows,
/// so the paper and the screen never disagree. Regenerate with LA_UPDATE_DOCS=1.
/// </summary>
public class UserGuideAppendixTests
{
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    internal static string Generate()
    {
        var s = new StringBuilder();
        s.AppendLine("# Anexă: verificările controlului și codurile, pe înțeles");
        s.AppendLine();
        s.AppendLine("> Generată automat din textele pe care aplicația le arată pe ecran (`CheckGuides`, `CodeExplainer`). Nu se editează de mână:");
        s.AppendLine("> se schimbă textul în aplicație și se regenerează (`LA_UPDATE_DOCS=1 dotnet test --filter UserGuideAppendixTests`).");
        s.AppendLine();
        s.AppendLine("## Verificările controlului stației");
        s.AppendLine();
        foreach (var id in CheckGuides.Ids.OrderBy(i => i[0] switch { 'C' => 0, 'P' => 1, 'A' => 2, 'U' => 3, 'D' => 4, 'N' => 5, _ => 6 }).ThenBy(i => i))
        {
            var g = CheckGuides.For(id);
            s.AppendLine($"### {id}");
            s.AppendLine();
            s.AppendLine($"- **Ce verificăm:** {g.What}");
            s.AppendLine($"- **De ce contează:** {g.Why}");
            s.AppendLine($"- **Cum verifică aplicația:** {g.How}");
            s.AppendLine($"- **Sursa tehnică:** `{g.Source}`");
            s.AppendLine($"- **Cum se citește rezultatul:** {g.Meaning}");
            s.AppendLine();
        }
        s.AppendLine("## Ce înseamnă rezultatele");
        s.AppendLine();
        foreach (var st in new[] { ControlStatus.Conform, ControlStatus.Neconform, ControlStatus.DeVerificat, ControlStatus.Nedeterminat })
        {
            var e = CheckGuides.Explain(new ControlCheck("C01", "", "", st, "", [], ""));
            s.AppendLine($"- {e.StatusMeaning}");
        }
        s.AppendLine();
        s.AppendLine("## Codurile care apar în probe");
        s.AppendLine();
        s.AppendLine("| Cod | Tip | Ce este | De ce contează | Când apare / valori |");
        s.AppendLine("|---|---|---|---|---|");
        var seen = new HashSet<string>();
        foreach (var id in CheckGuides.Ids.OrderBy(i => i))
            foreach (var c in CodeExplainer.FindIn(CheckGuides.For(id).Source))
                if (seen.Add(c.Code))
                    s.AppendLine($"| {Cell(c.Code)} | {c.Kind} | {Cell(c.What)} | {Cell(c.Why)} | {Cell(c.When)} |");
        return s.ToString().ReplaceLineEndings("\n");
    }

    private static string Cell(string t) => t.Replace("|", "\\|").Replace("\\", "\\\\").Replace("\\\\|", "\\|");

    [Fact]
    public void The_annex_in_the_user_guide_matches_what_the_application_shows()
    {
        var path = Path.Combine(Root(), "docs", "utilizare", "ANEXA_VERIFICARI_SI_CODURI.md");
        var expected = Generate();
        if (Environment.GetEnvironmentVariable("LA_UPDATE_DOCS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected, new UTF8Encoding(false));
        }
        Assert.True(File.Exists(path), "annex missing: run with LA_UPDATE_DOCS=1");
        Assert.True(File.ReadAllText(path).ReplaceLineEndings("\n") == expected, "the annex is out of date: run with LA_UPDATE_DOCS=1");
    }
}
