using System.IO;
using System.Linq;
using System.Data;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Lab 2026-11-10: grid rows whose item has no text of its own were announced by a screen reader as the class name
/// ("System.Data.DataRowView", "LogAnalyzer.Dfir.Windows.Containment.ProcessIncident"). The row is named after its first columns.
/// </summary>
public class GridRowNamesTests
{
    private sealed class NoText { public string Name { get; set; } = ""; public int Pid { get; set; } public string? Path { get; set; } }
    private sealed record WithText(string Name);
    private sealed class OwnText { public override string ToString() => "proces updsvc.exe"; }

    private static readonly (string, string)[] Cols = [("Proces", "Name"), ("PID", "Pid"), ("Cale", "Path"), ("Fără legătură", ""), ("Calculat", "A.B")];

    [Fact]
    public void An_item_without_its_own_text_is_named_after_its_first_columns_with_a_value()
    {
        var name = GridRowNames.NameFor(new NoText { Name = "updsvc.exe", Pid = 4120 }, Cols);
        Assert.Equal("Proces: updsvc.exe; PID: 4120", name);   // the empty path, the unbound column and the nested path add nothing
        Assert.DoesNotContain("NoText", name);
    }

    [Fact]
    public void A_table_row_is_named_after_its_cells_never_as_DataRowView()
    {
        var t = new DataTable();
        t.Columns.Add("Persoana"); t.Columns.Add("Conturi"); t.Columns.Add("Sid"); t.Columns.Add("A"); t.Columns.Add("B");
        t.Rows.Add("Administrator", "Administrator", "S-1-5-21-1-2-3-500", "x", "y");
        var name = GridRowNames.NameFor(t.DefaultView[0], [("Persoana", "Persoana"), ("Conturi", "[Conturi]"), ("Identificatori cont (SID)", "Sid"), ("A", "A"), ("B", "B")]);
        Assert.Equal("Persoana: Administrator; Conturi: Administrator; Identificatori cont (SID): S-1-5-21-1-2-3-500; A: x", name);   // at most four columns
        Assert.DoesNotContain("DataRowView", name);
    }

    [Fact]
    public void An_item_with_its_own_text_keeps_it()
    {
        Assert.Null(GridRowNames.NameFor(new OwnText(), Cols));
        // a record prints "WithText { Name = x }": a dump, not a text for a person (lab: "ControlCheck { Id = DM10, ... List`1[System.String] }")
        Assert.Equal("Proces: x", GridRowNames.NameFor(new WithText("x"), Cols));
        Assert.Null(GridRowNames.NameFor("text simplu", Cols));
    }

    [Fact]
    public void A_row_with_no_readable_column_still_gets_a_neutral_name_and_long_values_are_cut()
    {
        Assert.Equal("rând de tabel", GridRowNames.NameFor(new NoText(), [("X", "Missing")]));
        var name = GridRowNames.NameFor(new NoText { Name = new string('a', 500) }, Cols)!;
        Assert.True(name.Length < 120);
        Assert.Contains("…; PID: 0", name);   // the cut value, then the next column
    }
    [Fact]
    public void The_global_hook_is_SizeChanged_never_Loaded_and_the_application_registers_it()
    {
        // A class handler for Loaded is never called (WPF raises Loaded only where an instance handler exists): the first version did nothing on the VM.
        // A realised grid row needs a window handle, and creating one inside a test ends the test process early (632 of 1,036 tests ran), so the
        // registration is checked in the source; the behaviour is checked on the lab VM through UI Automation.
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "LogAnalyzer.App", "Services"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!, "LogAnalyzer.App", "Services", "GridRowNames.cs"));
        Assert.Contains("RegisterClassHandler(typeof(DataGridRow), FrameworkElement.SizeChangedEvent", src);
        Assert.DoesNotContain("RegisterClassHandler(typeof(DataGridRow), FrameworkElement.LoadedEvent", src);
        Assert.Contains("GridRowNames.Register();", File.ReadAllText(Path.Combine(dir!, "LogAnalyzer.App", "App.xaml.cs")));
    }
}