using System.IO;
using LogAnalyzer.UI.Views.Controls;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Design step 1 (docs/design/PLAN.md): at 1024 px the header needed about 1,150 px, so „Ce înseamnă?” and „Notițe” were outside the window and a
/// scrollbar line showed under the header. Below 1280 px the auxiliary labels fold into icons; every folded control keeps its accessible name.
/// </summary>
public class HeaderFitsNarrowWindowTests
{
    private static string MainWindowXaml()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "LogAnalyzer.App", "MainWindow.xaml"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, "LogAnalyzer.App", "MainWindow.xaml"));
    }

    [Theory]
    [InlineData(1000, "1280", false)]
    [InlineData(1279.9, "1280", false)]
    [InlineData(1280, "1280", true)]
    [InlineData(1920, "1280", true)]
    [InlineData(1000, "<1280", true)]
    [InlineData(1280, "<1280", false)]
    [InlineData(1100, "<1180", true)]
    [InlineData(1180, "1180", true)]
    public void A_threshold_shows_the_element_on_its_side_of_the_width(double width, string parameter, bool visible) =>
        Assert.Equal(visible, WidthThresholdConverter.IsVisible(width, parameter));

    [Theory]
    [InlineData(double.NaN, "1280")]
    [InlineData(1000, null)]
    [InlineData(1000, "")]
    [InlineData(1000, "lat")]
    [InlineData(1000, "<")]
    public void A_bad_width_or_parameter_never_hides_a_control(double width, string? parameter) =>
        Assert.True(WidthThresholdConverter.IsVisible(width, parameter));

    [Fact]
    public void The_folded_header_controls_keep_their_names_and_the_search_has_a_narrow_form()
    {
        var x = MainWindowXaml();
        // the two help buttons are icon + text; the text folds, the accessible name stays
        Assert.Contains("AutomationProperties.Name=\"Ce înseamnă?\"", x);
        Assert.Contains("AutomationProperties.Name=\"Notițe\"", x);
        Assert.Contains("<TextBlock Text=\"Ce înseamnă?\"", x);
        Assert.Contains("<TextBlock Text=\"Notițe\"", x);
        // the search box has a one-button form for a narrow window, bound to the same command palette
        Assert.Contains("AutomationProperties.Name=\"Căutare (Ctrl+K)\"", x);
        Assert.Contains("ConverterParameter=&lt;1180}", x);
        Assert.Contains("ConverterParameter=1180}", x);
        // labels that only repeat what the chip already says fold below the wide threshold
        foreach (var label in new[] { "Text=\"Stația: \"", "Text=\"Utilizator: \"", "Text=\"Limbaj: \"" })
        {
            int i = x.IndexOf(label, StringComparison.Ordinal);
            Assert.True(i > 0, label);
            Assert.Contains("ConverterParameter=1280}", x.Substring(i, x.IndexOf("/>", i, StringComparison.Ordinal) - i));
        }
        // icons are glyphs of the system icon font, never emoji
        Assert.Contains("FontFamily=\"{StaticResource IconFontFamily}\"", x);
    }
}
