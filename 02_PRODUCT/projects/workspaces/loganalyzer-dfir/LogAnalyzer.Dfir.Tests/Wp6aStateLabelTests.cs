using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Graph;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP6a / U7: one mapping gives the Romanian label of every state; the grid, the card, the graph and the reports all go through it.</summary>
public class Wp6aStateLabelTests
{
    [Fact]
    public void Every_one_of_the_ten_states_has_a_label_and_a_distinct_meaning()
    {
        var states = Enum.GetValues<StandardState>();
        Assert.Equal(10, states.Length);
        Assert.Equal(10, states.Select(StateLabels.Romanian).Distinct().Count());
        foreach (var s in states)
            Assert.False(string.IsNullOrWhiteSpace(StateLabels.Meaning(s)), s.ToString());
        Assert.Equal(10, states.Select(StateLabels.Meaning).Distinct().Count());
    }

    [Theory]
    [InlineData(Classification.Direct, "Observat")]
    [InlineData(Classification.Correlated, "Corelat")]
    [InlineData(Classification.Candidate, "Deducție")]
    [InlineData(Classification.Unproven, "Nedemonstrat")]
    [InlineData(Classification.BenignKnown, "Observat")]
    public void A_classification_is_shown_through_the_same_mapping_as_the_finding_state(Classification c, string label)
    {
        Assert.Equal(label, StateLabels.ForClassification(c));
        Assert.Equal(StateLabels.Romanian(FindingContract.StatusFor(c)), StateLabels.ForClassification(c));
    }

    [Fact]
    public void Graph_edges_carry_the_romanian_state_label_not_an_ad_hoc_english_word()
    {
        var g = new EvidenceGraph();
        var a = g.Add("File", @"C:\t\a.exe");
        var b = g.Add("File", @"C:\t\b.exe");
        g.LinkStored(a, RelationType.DerivedFrom, b, Timestamp.Unknown(), "EV-1", "row=1", "derivat din test", Classification.Candidate, Confidence.Low, "test");
        var row = Assert.Single(GraphExplorer.EdgesOf(g, a.Id));
        Assert.Equal("Deducție", row.Classification);
    }

    [Fact]
    public void No_view_or_converter_keeps_an_english_wording_path_for_states()
    {
        var conv = Wp6aXamlTests.Read("Views", "Converters.cs");
        Assert.DoesNotContain("GraphExplorer.Wording", conv);
        Assert.Contains("StateLabels.ForClassification", conv);
        var graphSrc = File.ReadAllText(Path.Combine(Wp6aXamlTests.AppDir(), "..", "LogAnalyzer.Dfir.Core", "Graph", "GraphExplorer.cs"));
        Assert.DoesNotContain("\"OBSERVED\"", graphSrc);
        Assert.DoesNotContain("public static string Wording", graphSrc);
    }

    [Fact]
    public void The_verification_line_shown_to_the_operator_uses_the_romanian_labels_and_keeps_the_spec_name()
    {
        var counts = LogAnalyzer.Verification.VerificationReport.CountVerdicts([StandardState.Verified, StandardState.Unproven, StandardState.Unproven, StandardState.Rejected]);
        var line = LogAnalyzer.Verification.VerificationReport.LineRomanian(counts);
        Assert.Equal("Verificare: 1 Verificat (VERIFIED), 2 Nedemonstrat (UNPROVEN), 1 Respins (REJECTED)", line);
        Assert.Equal("Verificare: nicio constatare de verificat", LogAnalyzer.Verification.VerificationReport.LineRomanian(LogAnalyzer.Verification.VerificationReport.CountVerdicts([])));
    }
}
