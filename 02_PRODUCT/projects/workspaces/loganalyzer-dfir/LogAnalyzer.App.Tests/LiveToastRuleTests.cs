using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>The live notification card opens for medium severity and above; low and informational alerts go to the status bar only.</summary>
public sealed class LiveToastRuleTests
{
    [Theory]
    [InlineData("critical", true)]
    [InlineData("High", true)]
    [InlineData("medium", true)]
    [InlineData("low", false)]
    [InlineData("Info", false)]
    [InlineData("informational", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_medium_and_above_open_the_card(string? severity, bool expected) => Assert.Equal(expected, MainViewModel.ShouldShowToast(severity));
}
