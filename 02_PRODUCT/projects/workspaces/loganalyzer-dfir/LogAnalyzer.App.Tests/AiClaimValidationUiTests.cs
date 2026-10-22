using System.Collections.ObjectModel;
using LogAnalyzer.Dfir.AI;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// R22.5, WP4, screen side: the AI screen binds validated rows, never the raw model statements. A statement without a validation is shown NEVERIFICABIL
/// with a warning; only SUSTINUT has no warning; NESUSTINUT is struck. The rest of the output registry is in LogAnalyzer.Dfir.Tests (AiClaimValidationReportTests).
/// </summary>
public sealed class AiClaimValidationUiTests
{
    private static AiStatement Statement(ValidatedClaim? v) => new("summary", "Procesul evil.exe a rulat.", [new Citation("F-0001", "finding", [])]) { Validation = v };

    [Fact]
    public void The_AI_screen_collections_hold_validated_rows_not_raw_model_statements()
    {
        var p = typeof(AiAnalysisViewModel).GetProperty(nameof(AiAnalysisViewModel.AiStatements))!;
        Assert.Equal(typeof(ObservableCollection<AiClaimRow>), p.PropertyType);
        Assert.DoesNotContain(typeof(AiAnalysisViewModel).GetProperties(), x => x.PropertyType.IsGenericType && x.PropertyType.GetGenericArguments().Contains(typeof(AiStatement)));
        Assert.DoesNotContain(typeof(AiAnalysisViewModel).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public),
            x => x.FieldType.IsGenericType && x.FieldType.GetGenericArguments().Contains(typeof(AiStatement)));
    }

    [Fact]
    public void A_statement_without_validation_is_shown_NEVERIFICABIL_with_a_warning()
    {
        var row = AiClaimRow.From(Statement(null));
        Assert.StartsWith("NEVERIFICABIL", row.State);
        Assert.NotEmpty(row.Warning);
        Assert.False(row.IsStruck);
    }

    [Theory]
    [InlineData(ClaimState.Sustinut, false, false)]
    [InlineData(ClaimState.Partial, true, false)]
    [InlineData(ClaimState.Neverificabil, true, false)]
    [InlineData(ClaimState.Nesustinut, true, true)]
    public void Only_SUSTINUT_is_shown_without_a_warning_and_NESUSTINUT_is_struck(ClaimState state, bool warning, bool struck)
    {
        var row = AiClaimRow.From(Statement(new ValidatedClaim("summary", "t", ["F-0001"], state, state == ClaimState.Sustinut ? "" : "motivul", [])));
        Assert.Equal(state.ToSpec(), row.State);
        Assert.Equal(warning, row.Warning.Length > 0);
        Assert.Equal(struck, row.IsStruck);
        if (warning) Assert.Contains("motivul", row.Warning);
    }
}
