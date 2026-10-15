using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Round-3 security review of PR #289: the signature appearance cannot cover the report, the update xref is exact, provenance of the anchor code. Test material only.</summary>
public sealed partial class OfficerQualifiedSignatureTests
{
    /// <summary>Replaces the appearance stream of the signature (and its BBox) with <paramref name="content"/>.</summary>
    private static string WithAppearance(string body, string content, string? bbox = null)
    {
        if (!body.Contains("/Subtype /Form", StringComparison.Ordinal)) return body;
        var head = body[..body.IndexOf("stream\n", StringComparison.Ordinal)];
        head = Regex.Replace(head, @"/Length \d+", $"/Length {Encoding.Latin1.GetByteCount(content)}");
        if (bbox is not null) head = Regex.Replace(head, @"/BBox \[[^\]]*\]", $"/BBox {bbox}");
        return head + "stream\n" + content + "endstream";
    }

    [Fact]
    public void R3_the_reviewers_overlay_probe_quarter_page_widget_with_white_box_text_and_image_is_not_OFICIAL()
    {
        using var s = NewSetup();
        const string overlay = "q 1 g 0 0 297 421 re f Q BT /F1 14 Tf 20 300 Td (CONCLUZIE: CONFORM - nicio neconformitate) Tj ET BI /W 1 /H 1 /BPC 8 /CS /G ID \0 EI\n";
        var signed = SignWith(s, rewrite: (num, body) =>
            body.Contains("/Subtype /Widget", StringComparison.Ordinal) ? Regex.Replace(body, @"/Rect \[[^\]]*\]", "/Rect [0 421 297 842]")
            : WithAppearance(body, overlay, "[0 0 297 421]"));
        Assert.NotEqual(OfficialVerdict.Oficial, VerifyWithPaper(s, signed).Verdict);
        Assert.Equal(OfficialVerdict.Invalid, VerifyWithPaper(s, signed).Verdict);
    }

    [Theory]
    [InlineData("BT /F1 8 Tf 5 5 Td (ok) Tj ET BI /W 1 /H 1 /BPC 8 /CS /G ID \0 EI\n")]
    [InlineData("q /Im1 Do Q\n")]
    [InlineData("/GS1 gs BT /F1 8 Tf 5 5 Td (x) Tj ET\n")]
    public void R3_images_XObject_calls_and_graphics_states_in_the_appearance_are_refused(string content)
    {
        using var s = NewSetup();
        AssertModified(VerifyWithPaper(s, SignWith(s, rewrite: (n, b) => WithAppearance(b, content))));
    }

    [Fact]
    public void R3_an_appearance_larger_than_the_reserved_box_or_with_a_matrix_is_refused()
    {
        using var s = NewSetup();
        AssertModified(VerifyWithPaper(s, SignWith(s, rewrite: (n, b) => WithAppearance(b, "BT ET\n", "[0 0 595 842]"))));
        AssertModified(VerifyWithPaper(s, SignWith(s, rewrite: (n, b) => b.Contains("/Subtype /Form") ? b.Replace("/BBox", "/Matrix [2 0 0 2 0 0] /BBox") : b)));
    }

    [Fact]
    public void R3_the_generated_report_already_holds_the_empty_reserved_field_over_the_box_on_its_last_page()
    {
        var report = Report();
        var st = PdfStructure.Parse(report, 2);
        Assert.Equal(2, st.Sections);
        var field = SignatureField.Find(st);
        Assert.NotNull(field);
        Assert.Equal(SignatureField.Rect(842), field.Value.Rect);
        var widget = st.Dict(field.Value.Widget);
        Assert.DoesNotContain("/V ", widget);
        Assert.Contains($"/P {st.Pages[^1]} 0 R", widget);
        Assert.Contains($"/Annots [{field.Value.Widget} 0 R]", st.Dict(st.Pages[^1]));
        Assert.True(st.Pages.Count >= 2);                                      // the box has a page of its own
    }

    [Fact]
    public void R3_a_report_without_the_reserved_field_is_not_signed_and_signing_never_touches_page_or_catalog()
    {
        using var s = NewSetup();
        var text = Encoding.Latin1.GetString(s.Original);
        int firstEof = text.IndexOf("%%EOF\n", StringComparison.Ordinal) + 6;
        var unreserved = s.Original.AsSpan(0, firstEof).ToArray();
        var ex = Assert.Throws<NotSupportedException>(() => OfficerFlows.Sign(unreserved, s.Officer, "ST-1", "r", Now));
        Assert.Contains("câmpul de semnătură rezervat", ex.Message);

        var st = PdfStructure.Parse(s.Original, 2);
        var pageChanged = SignWith(s, extra: new Dictionary<int, string> { [st.Pages[^1]] = st.Dict(st.Pages[^1]).Replace("/Tabs /S", "/Tabs /R") });
        AssertModified(VerifyWithPaper(s, pageChanged));
        var catalogChanged = SignWith(s, extra: new Dictionary<int, string> { [st.Root] = st.Dict(st.Root).Replace(">>", " /PageMode /UseNone>>") });
        AssertModified(VerifyWithPaper(s, catalogChanged));
        var rectChanged = SignWith(s, rewrite: (n, b) => b.Contains("/Subtype /Widget") ? Regex.Replace(b, @"/Rect \[[^\]]*\]", "/Rect [100 100 371 190]") : b);
        AssertModified(VerifyWithPaper(s, rectChanged));
    }

    [Theory]
    [InlineData("comment")]
    [InlineData("19-byte entries")]
    [InlineData("decimal offset")]
    [InlineData("root generation")]
    [InlineData("decimal prev")]
    public void R3_the_update_xref_follows_the_exact_classic_grammar(string variant)
    {
        using var s = NewSetup();
        Func<string, string> mutate = variant switch
        {
            "comment" => t => t.Replace("xref\n", "xref\n% comentariu\n"),
            "19-byte entries" => t => t.Replace(" n \n", " n\n"),
            "decimal offset" => t => Regex.Replace(t, @"(\d{10}) 00000 n \n", m => $"{long.Parse(m.Groups[1].Value)}.0 00000 n \n", RegexOptions.None, TimeSpan.FromSeconds(1)),
            "root generation" => t => Regex.Replace(t, @"/Root (\d+) 0 R", "/Root $1 1 R"),
            _ => t => Regex.Replace(t, @"/Prev (\d+)", "/Prev $1.0"),
        };
        var signed = PdfIncrementalSigner.Sign(s.Original, Look, new SignedCmsSigner(s.Officer), false, null, rewriteXrefAndTrailer: mutate);
        Assert.Equal(OfficialVerdict.Invalid, VerifyWithPaper(s, signed).Verdict);
    }

    [Theory]
    [InlineData("/Filter /FlateDecode /DecodeParms <</Predictor 12>>")]
    [InlineData("/Filter /LZWDecode")]
    public void R3_font_files_with_other_filters_or_decode_parameters_are_refused(string filter)
    {
        using var s = NewSetup();
        var st = PdfStructure.Parse(s.Original, 2);
        int fd = st.Size + 10, ff = st.Size + 11;
        var signed = SignWith(s,
            extra: new Dictionary<int, string> { [fd] = $"<</Type /FontDescriptor /FontName /X /Flags 32 /FontFile2 {ff} 0 R>>", [ff] = $"<</Length 0 {filter}>>\nstream\n\nendstream" },
            rewrite: (n, b) => b.Contains("/BaseFont /Helvetica") ? b.Replace("/BaseFont /Helvetica", $"/BaseFont /Helvetica /FontDescriptor {fd} 0 R") : b);
        AssertModified(VerifyWithPaper(s, signed));
    }

    [Fact]
    public void R3_our_signature_only_fills_the_reserved_field()
    {
        using var s = NewSetup();
        var signed = OfficerFlows.Sign(s.Original, s.Officer, "ST-1", "r", Now);
        var inspection = PdfUpdateInspector.Inspect(signed, s.Original.Length);
        Assert.Null(inspection.Problem);
        Assert.Equal(["appearance", "font", "sig", "widget"], inspection.Roles.Values.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(OfficialVerdict.Oficial, VerifyWithPaper(s, signed).Verdict);
    }

    [Fact]
    public void R3_the_anchor_code_counts_as_from_the_station_only_after_the_explicit_button_and_typing_clears_it()
    {
        var vm = new StationControlViewModel();
        vm.PreviousAnchorCode = "AAAAA-AAAAA-AAAAA-AAAAA";            // typed from the paper, even if equal to what the station remembers
        Assert.False(vm.PreviousCodeFromStation);
        vm.ApplyStationCode("BBBBB-BBBBB-BBBBB-BBBBB");
        Assert.True(vm.PreviousCodeFromStation);
        Assert.Equal("BBBBB-BBBBB-BBBBB-BBBBB", vm.PreviousAnchorCode);
        vm.PreviousAnchorCode = "BBBBB-BBBBB-BBBBB-BBBBC";            // any edit by the officer
        Assert.False(vm.PreviousCodeFromStation);
    }
}
