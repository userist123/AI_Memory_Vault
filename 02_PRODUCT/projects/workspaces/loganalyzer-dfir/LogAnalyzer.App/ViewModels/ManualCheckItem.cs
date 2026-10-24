using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.Dfir.Flow;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>One question the controller answers by looking (seals, BIOS password, documents): Da / NU / Nu pot verifica, plus a note.</summary>
    public sealed partial class ManualCheckItem : ObservableObject
    {
        public ManualCheckItem(ManualCheck check) { Check = check; }
        public ManualCheck Check { get; }

        [ObservableProperty] private ManualAnswer _answer = ManualAnswer.NotAnswered;
        [ObservableProperty] private string _note = "";

        public bool IsYes { get => Answer == ManualAnswer.Yes; set { if (value) Answer = ManualAnswer.Yes; } }
        public bool IsNo { get => Answer == ManualAnswer.No; set { if (value) Answer = ManualAnswer.No; } }
        public bool IsCannot { get => Answer == ManualAnswer.CannotCheck; set { if (value) Answer = ManualAnswer.CannotCheck; } }
        public string GroupName => "manual-" + Check.Id;

        partial void OnAnswerChanged(ManualAnswer value)
        {
            OnPropertyChanged(nameof(IsYes)); OnPropertyChanged(nameof(IsNo)); OnPropertyChanged(nameof(IsCannot));
        }

        public ManualCheckResult ToResult() => new(Check.Id, Answer, Note.Trim());
    }
}
