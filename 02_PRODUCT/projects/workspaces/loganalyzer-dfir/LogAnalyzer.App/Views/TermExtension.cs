using System;
using System.Windows.Markup;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// XAML access to the plain-language glossary (WP6a, R18): <c>{views:Term evtx}</c> gives the human phrase, <c>{views:Term evtx, Tooltip=True}</c> the
    /// technical name with its explanation, <c>{views:Term evtx, Advanced=True}</c> the technical name with the human phrase beside it.
    /// The text is bound to the language setting, so it changes with the language. A term that is not in the glossary is shown unchanged.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class TermExtension : MarkupExtension
    {
        public TermExtension() { }
        public TermExtension(string term) { Term = term; }

        [ConstructorArgument("term")]
        public string Term { get; set; } = "";
        public bool Tooltip { get; set; }
        public bool Advanced { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // Bound to the language setting (WP6b), so the phrase changes with the language. An unknown term is shown unchanged.
            var term = Glossary.Find(Term)?.Key;
            if (term is null) return Term;
            if (Tooltip) return LocBinding.Create($"{LocSource.GlossaryPrefix}{term}.tip").ProvideValue(serviceProvider);
            var key = $"{LocSource.GlossaryPrefix}{term}{(Advanced ? ".adv" : "")}";
            return LocBinding.Create(key).ProvideValue(serviceProvider);
        }
    }
}
