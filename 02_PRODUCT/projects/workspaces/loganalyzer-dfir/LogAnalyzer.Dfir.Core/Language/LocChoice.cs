using System.ComponentModel;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// One entry of a drop-down whose text is a resource-layer key. <see cref="Text"/> follows the language (it raises <c>PropertyChanged</c> when the language
/// changes), so a ComboBox keeps its selected index and still switches language without a restart.
/// </summary>
public sealed class LocChoice : INotifyPropertyChanged
{
    private readonly EventHandler _handler;

    public LocChoice(string key)
    {
        Key = key;
        _handler = (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        Loc.LanguageChanged += _handler;
    }

    public string Key { get; }
    public string Text => Loc.T(Key);
    public override string ToString() => Text;
    public event PropertyChangedEventHandler? PropertyChanged;
}
