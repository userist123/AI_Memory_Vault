using System.Collections.Generic;
using System.ComponentModel;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.Services
{
    /// <summary>One entry of the language selector. The name is the language's own name and is never translated.</summary>
    public sealed record LanguageOption(AppLanguage Language, string NativeName);

    /// <summary>
    /// What the language selector in the title bar and on the sign-in window binds to (WP6b): the list, and the selected language. Choosing a language
    /// changes every <c>{loc:T ...}</c> text at once and stores the choice per user; no restart.
    /// </summary>
    public sealed class LanguageChoice : INotifyPropertyChanged
    {
        public static LanguageChoice Instance { get; } = new();

        public static IReadOnlyList<LanguageOption> Options { get; } =
        [
            new(AppLanguage.Romanian, "Română"),
            new(AppLanguage.English, "English"),
        ];

        private readonly LanguageSelection _selection = new(new UserSettings(UserSettings.DefaultFile()));

        private LanguageChoice() { Loc.LanguageChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Called once at startup, before any window: applies the stored language (default Romanian).</summary>
        public void ApplyStored() => _selection.Apply();

        public AppLanguage Selected
        {
            get => Loc.Current;
            set { if (value != Loc.Current) _selection.Choose(value); }
        }
    }
}
