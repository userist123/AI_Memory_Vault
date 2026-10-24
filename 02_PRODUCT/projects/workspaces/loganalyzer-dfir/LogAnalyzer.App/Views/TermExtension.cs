using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// XAML access to the plain-language glossary (WP6a, R18): <c>{views:Term evtx}</c> gives the human phrase, <c>{views:Term evtx, Tooltip=True}</c> the
    /// technical name with its explanation, <c>{views:Term evtx, Advanced=True}</c> the technical name with the human phrase beside it.
    /// <see cref="Suffix"/> is appended to the phrase (e.g. a compliance tag). A term that is not in the glossary is shown unchanged.
    /// WP18 S3: without <see cref="Advanced"/> the phrase follows the language level of the session (Simple = human phrase, Expert = technical name
    /// with the human phrase beside it) and is recomputed when the level changes, through a binding on <see cref="TermSource"/>.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TermExtension : MarkupExtension
    {
        public TermExtension() { }
        public TermExtension(string term) { Term = term; }

        [ConstructorArgument("term")]
        public string Term { get; set; } = "";
        public bool Tooltip { get; set; }
        /// <summary>Forces the technical form regardless of the language level (advanced views).</summary>
        public bool Advanced { get; set; }
        public string Suffix { get; set; } = "";

        /// <summary>The text for the current language level (also used by tests and by code that has no binding target).</summary>
        public string Resolve() => Tooltip ? Glossary.Tooltip(Term) : Glossary.Display(Term, Advanced || LanguageLevelContext.IsExpert) + Suffix;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (Tooltip || Advanced) return Resolve();   // these do not change with the level
            if (serviceProvider.GetService(typeof(IProvideValueTarget)) is not IProvideValueTarget { TargetObject: System.Windows.DependencyObject, TargetProperty: System.Windows.DependencyProperty })
                return Resolve();                        // not a bindable target (e.g. inside a style setter): fixed text
            var binding = new Binding(nameof(TermSource.Version)) { Source = TermSource.Instance, Mode = BindingMode.OneWay, Converter = new TermConverter(this) };
            return binding.ProvideValue(serviceProvider);
        }

        private sealed class TermConverter : IValueConverter
        {
            private readonly TermExtension _term;
            public TermConverter(TermExtension term) => _term = term;
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => _term.Resolve();
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }
    }

    /// <summary>Change notifier for every glossary text on screen: bumps <see cref="Version"/> when the language level changes.</summary>
    public sealed class TermSource : INotifyPropertyChanged
    {
        public static TermSource Instance { get; } = new();
        private int _version;
        private TermSource() => LanguageLevelContext.Changed += () => { _version++; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version))); };
        public int Version => _version;
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
