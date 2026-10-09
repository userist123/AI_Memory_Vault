using System.ComponentModel;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// What the XAML binds to for text (WP6b): <c>Binding Source={x:Static LocSource.Instance}, Path=[key]</c>. When the language changes it raises
/// <c>Item[]</c>, so every bound text updates without a restart. Keys that start with <c>@term:</c> read the glossary
/// (<c>@term:evtx</c> the phrase, <c>@term:evtx:tip</c> the tooltip, <c>@term:evtx:adv</c> the technical name with the phrase beside it).
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource() { Loc.LanguageChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Resolve(key);

    public static string Resolve(string key)
    {
        if (!key.StartsWith("@term:", StringComparison.Ordinal)) return Loc.T(key);
        var parts = key["@term:".Length..].Split(':');
        return parts.Length > 1
            ? parts[1] switch { "tip" => Glossary.Tooltip(parts[0]), "adv" => Glossary.Display(parts[0], advanced: true), _ => Glossary.Display(parts[0], advanced: false) }
            : Glossary.Display(parts[0], advanced: false);
    }
}
