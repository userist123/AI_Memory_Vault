using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace LogAnalyzer
{
    /// <summary>
    /// Research backlog #3: every <c>container.Text(string)</c> written in a LogAnalyzer namespace resolves to this overload (an extension method
    /// declared in an enclosing namespace is found before the one imported with <c>using QuestPDF.Fluent</c>), so evidence-derived text printed in
    /// a PDF report passes through <see cref="LogAnalyzer.Dfir.Language.ReportText.Clean"/>. Dfir.Tests checks on the compiled IL that no report
    /// calls QuestPDF's overload directly.
    /// </summary>
    internal static class ReportPdfText
    {
        public static TextBlockDescriptor Text(this IContainer container, string? text) =>
            TextExtensions.Text(container, LogAnalyzer.Dfir.Language.ReportText.Clean(text));
    }
}
