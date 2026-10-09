using System;
using System.Windows.Markup;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// XAML access to the plain-language glossary (WP6a, R18): <c>{views:Term evtx}</c> gives the human phrase, <c>{views:Term evtx, Tooltip=True}</c> the
    /// technical name with its explanation, <c>{views:Term evtx, Advanced=True}</c> the technical name with the human phrase beside it.
    /// <see cref="Suffix"/> is appended to the phrase (e.g. a compliance tag). A term that is not in the glossary is shown unchanged.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TermExtension : MarkupExtension
    {
        public TermExtension() { }
        public TermExtension(string term) { Term = term; }

        [ConstructorArgument("term")]
        public string Term { get; set; } = "";
        public bool Tooltip { get; set; }
        public bool Advanced { get; set; }
        public string Suffix { get; set; } = "";

        public override object ProvideValue(IServiceProvider serviceProvider) =>
            Tooltip ? Glossary.Tooltip(Term) : Glossary.Display(Term, Advanced) + Suffix;
    }
}
