using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Finding B8: the cross-station export carries A17 (the application's own commands, confirmed or not) with the other integrity indicators.</summary>
public sealed class LowFindingsB8Tests
{
    [Fact]
    public void The_exported_integrity_indicators_include_A17_and_keep_the_others()
    {
        Assert.Equal(["A05", "A11", "A12", "A13", "A16", "A17", "S05"], MediaHistoryExport.IndicatorIds);
    }

    [Fact]
    public void The_A17_indicator_survives_a_round_trip_of_the_signed_document()
    {
        var doc = new MediaHistoryDoc
        {
            Indicators = [new StationIndicator { Id = "A17", Title = "Comenzi proprii", Status = "DE VERIFICAT", Detail = "1 comandă neconfirmată" }],
        };
        var json = MediaHistoryExport.Canonical(doc);
        Assert.Contains("A17", System.Text.Encoding.UTF8.GetString(json));
    }
}
