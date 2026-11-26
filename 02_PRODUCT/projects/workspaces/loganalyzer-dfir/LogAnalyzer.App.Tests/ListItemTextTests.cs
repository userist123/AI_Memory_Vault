using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Lab 2026-11-10 (Continent on a VM): the items of plain lists and trees were announced by a screen reader as their class name
/// ("LogAnalyzer.Core.Models.TimelineItem", "...MitreTechnique", "...AptActorProfile", "...ProcessNode"): such an item is named after its text.
/// </summary>
public class ListItemTextTests
{
    [Fact]
    public void List_and_tree_items_have_a_text_a_person_can_read()
    {
        var items = new object[]
        {
            new TimelineItem { Timestamp = new System.DateTime(2026, 11, 6, 13, 53, 21), Title = "Serviciu nou instalat: UpdSvc", Severity = "Ridicat" },
            new AptActorProfile { ActorName = "APT29 (Cozy Bear)", MatchLevel = "Scăzut", MatchScore = 12.4 },
            new MitreTechnique { TechId = "T1543.003", Name = "Windows Service", Tactic = "Persistence" },
            new ProcessNode { ProcessName = "updsvc.exe", PID = 4120 },
        };
        foreach (var i in items)
        {
            var text = i.ToString()!;
            Assert.DoesNotContain("LogAnalyzer.", text);
            Assert.NotEqual(i.GetType().Name, text);
        }
        Assert.Equal("06.11.2026 13:53:21 · Serviciu nou instalat: UpdSvc · Ridicat", items[0].ToString());
        Assert.Equal("APT29 (Cozy Bear) · potrivire Scăzut (12%)", items[1].ToString());
        Assert.Equal("T1543.003 Windows Service · Persistence", items[2].ToString());
        Assert.Equal("updsvc.exe (PID 4120)", items[3].ToString());
    }
}
