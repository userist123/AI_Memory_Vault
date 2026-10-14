using System.Reflection;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Language;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Research backlog #3 (H8, Trojan Source): evidence-derived text printed in reports (PDF, HTML, CSV) never carries bidi controls, zero-width or
/// control characters that would make the report show something other than the evidence. They are removed and a visible marker names them.
/// </summary>
public sealed class ReportTextSanitiserTests
{
    [Theory]
    [InlineData("invoice\u202Etxt.exe", "invoicetxt.exe [caractere ascunse eliminate: U+202E]")]
    [InlineData("a\u2066b\u2069c", "abc [caractere ascunse eliminate: U+2066, U+2069]")]
    [InlineData("ad\u200Bmin", "admin [caractere ascunse eliminate: U+200B]")]
    [InlineData("\uFEFFuser", "user [caractere ascunse eliminate: U+FEFF]")]
    [InlineData("bell\u0007x\u0085y", "bellxy [caractere ascunse eliminate: U+0007, U+0085]")]
    [InlineData("rtl\u200Fmark\u061C", "rtlmark [caractere ascunse eliminate: U+061C, U+200F]")]
    public void Hidden_characters_are_removed_and_named(string input, string expected) =>
        Assert.Equal(expected, ReportText.Clean(input));

    [Theory]
    [InlineData("Ștefan Ţară — C:\\Users\\u\\Desktop\\a.txt")]
    [InlineData("line1\r\nline2\tcol")]
    [InlineData("")]
    public void Ordinary_text_is_returned_unchanged(string input) => Assert.Same(input, ReportText.Clean(input));

    [Fact]
    public void Null_becomes_empty() => Assert.Equal("", ReportText.Clean(null));

    [Fact]
    public void Csv_cells_are_sanitised()
    {
        var cell = CsvWriter.Escape("pay\u202Efdp.exe");
        Assert.DoesNotContain('\u202E', cell);
        Assert.Contains("[caractere ascunse eliminate: U+202E]", cell);
    }

    /// <summary>
    /// PDF reports: every QuestPDF <c>container.Text(string)</c> in the report assemblies resolves to the sanitising overload
    /// (<c>LogAnalyzer.ReportPdfText.Text</c>), never directly to QuestPDF's. Checked on the compiled IL, so a new report cannot bypass it.
    /// </summary>
    [Theory]
    [InlineData("LogAnalyzer.Dfir.Windows")]
    [InlineData("LogAnalyzer.Core")]
    public void Pdf_report_text_goes_through_the_sanitiser(string assemblyName)
    {
        var asm = Assembly.Load(assemblyName);
        var bypass = new List<string>();
        int sanitised = 0;
        foreach (var type in SafeTypes(asm))
        {
            if (type.FullName == "LogAnalyzer.ReportPdfText") continue;
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
            {
                byte[]? il;
                try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch (Exception ex) when (ex is InvalidOperationException or BadImageFormatException) { continue; }
                if (il is null) continue;
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x28 && il[i] != 0x6F) continue;      // call / callvirt
                    MethodBase? target;
                    try { target = m.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), SafeGenericArgs(m.DeclaringType), SafeMethodArgs(m)); }
                    catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or TypeLoadException or MissingMethodException) { continue; }
                    if (target is null || target.Name != "Text") continue;
                    var ps = target.GetParameters();
                    if (ps.Length != 2 || ps[1].ParameterType != typeof(string)) continue;
                    if (target.DeclaringType?.FullName == "QuestPDF.Fluent.TextExtensions") bypass.Add($"{type.FullName}.{m.Name}");
                    else if (target.DeclaringType?.FullName == "LogAnalyzer.ReportPdfText") sanitised++;
                }
            }
        }
        Assert.Empty(bypass);
        Assert.True(sanitised > 0, "no report text call found: the check did not run");
    }

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static Type[]? SafeGenericArgs(Type? t) => t is { IsGenericType: true } ? t.GetGenericArguments() : null;
    private static Type[]? SafeMethodArgs(MethodBase m) => m.IsGenericMethod ? m.GetGenericArguments() : null;
}
