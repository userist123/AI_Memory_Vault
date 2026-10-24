using System.ComponentModel;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// The language level (Simplu / Expert) as something XAML can bind to from any page, whatever its DataContext
    /// (<c>{Binding Source={x:Static services:LanguageLevelState.Instance}, Path=IsSimple}</c>). It reads <see cref="LanguageLevelContext"/> live, so it can never
    /// disagree with it, and raises the change when the header switch moves.
    /// </summary>
    public sealed class LanguageLevelState : INotifyPropertyChanged
    {
        public static LanguageLevelState Instance { get; } = new();

        private LanguageLevelState() => LanguageLevelContext.Changed += () =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSimple)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpert)));
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsSimple => !LanguageLevelContext.IsExpert;
        public bool IsExpert => LanguageLevelContext.IsExpert;
    }
}
