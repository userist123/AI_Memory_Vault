using System.ComponentModel;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// What the XAML binds to for text (WP6b): <c>Binding Source={x:Static LocSource.Instance}, Path=[key]</c>. When the language changes it raises
/// <c>Item[]</c>, so every bound text updates without a restart. Keys that start with <c>glossary.</c> read the glossary
/// (<c>glossary.evtx</c> the phrase, <c>glossary.evtx.tip</c> the tooltip, <c>glossary.evtx.adv</c> the technical name with the phrase beside it).
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource() { Loc.LanguageChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Resolve(key);

    /// <summary>Prefix of the glossary pseudo-keys: <c>glossary.evtx</c>, <c>glossary.evtx.tip</c>, <c>glossary.evtx.adv</c>.</summary>
    public const string GlossaryPrefix = "glossary.";

    public static string Resolve(string key)
    {
        if (!key.StartsWith(GlossaryPrefix, StringComparison.Ordinal)) return Loc.T(key);
        var rest = key[GlossaryPrefix.Length..];
        if (rest.EndsWith(".tip", StringComparison.Ordinal)) return Glossary.Tooltip(rest[..^4]);
        if (rest.EndsWith(".adv", StringComparison.Ordinal)) return Glossary.Display(rest[..^4], advanced: true);
        return Glossary.Display(rest, advanced: false);
    }
}
