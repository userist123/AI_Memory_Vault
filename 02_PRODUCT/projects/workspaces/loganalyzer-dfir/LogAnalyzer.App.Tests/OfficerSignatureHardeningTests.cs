using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Residuals after the round-4 review of PR #289: the appearance starts with a clip to its box, and font programs are decoded under a size limit.</summary>
public sealed partial class OfficerQualifiedSignatureTests
{
    [Fact]
    public void H1_our_appearance_starts_with_the_clip_to_its_box()
    {
        using var s = NewSetup();
        var signed = OfficerFlows.Sign(s.Original, s.Officer, "ST-1", "r", Now);
        var text = Encoding.Latin1.GetString(signed, s.Original.Length, signed.Length - s.Original.Length);
        int st = text.IndexOf("stream\n", text.IndexOf("/Subtype /Form", StringComparison.Ordinal), StringComparison.Ordinal) + 7;
        var w = SignatureField.BoxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var h = SignatureField.BoxHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith($"q 0 0 {w} {h} re W n", text[st..]);
        Assert.Equal(OfficialVerdict.Oficial, VerifyWithPaper(s, signed).Verdict);
    }

    [Theory]
    [InlineData("BT /F1 8 Tf 5 5 Td (fara decupare) Tj ET\n")]
    [InlineData("q 0 0 10 10 re W n BT ET Q\n")]
    [InlineData("0 0 271 90 re f q 0 0 271 90 re W n Q\n")]
    public void H1_an_appearance_that_does_not_start_with_the_clip_to_its_box_is_refused(string content)
    {
        using var s = NewSetup();
        AssertModified(VerifyWithPaper(s, SignWith(s, rewrite: (n, b) => WithAppearance(b, content))));
    }

    [Fact]
    public void H2_a_font_program_that_inflates_beyond_the_limit_is_refused()
    {
        using var s = NewSetup();
        byte[] bomb;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(new byte[5 * 1024 * 1024]);
            bomb = ms.ToArray();
        }
        var st = PdfStructure.Parse(s.Original, 2);
        int fd = st.Size + 10, ff = st.Size + 11;
        var data = Encoding.Latin1.GetString(bomb);
        var signed = SignWith(s,
            extra: new Dictionary<int, string> { [fd] = $"<</Type /FontDescriptor /FontName /X /Flags 32 /FontFile2 {ff} 0 R>>", [ff] = $"<</Length {bomb.Length} /Filter /FlateDecode>>\nstream\n{data}\nendstream" },
            rewrite: (n, b) => b.Contains("/BaseFont /Helvetica") ? b.Replace("/BaseFont /Helvetica", $"/BaseFont /Helvetica /FontDescriptor {fd} 0 R") : b);
        var v = VerifyWithPaper(s, signed);
        AssertModified(v);
        Assert.Contains("prea mare", v.Headline);
    }
}
