using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Presentation;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// View model of the finding card (WP6a, U4): the card data (<see cref="FindingCardModel"/>, built by Dfir.Core from the finding and the case) and which
    /// of the four action panels is open. A second click on the same action closes its panel. No logic about the finding lives here.
    /// </summary>
    public sealed partial class FindingCardViewModel : ObservableObject
    {
        public const string PanelWhy = "why";
        public const string PanelEvidence = "evidence";
        public const string PanelVerify = "verify";
        public const string PanelTodo = "todo";

        public FindingCardViewModel(FindingCardModel card) { Card = card; }

        public FindingCardModel Card { get; }

        /// <summary>One of the <c>Panel*</c> constants, or empty when no panel is open.</summary>
        [ObservableProperty] private string _activePanel = "";

        public bool IsWhyOpen => ActivePanel == PanelWhy;
        public bool IsEvidenceOpen => ActivePanel == PanelEvidence;
        public bool IsVerifyOpen => ActivePanel == PanelVerify;
        public bool IsTodoOpen => ActivePanel == PanelTodo;

        /// <summary>The „De ce?” text (observation, evidence, reasoning, limits, verification) as plain text.</summary>
        public string WhyText => Card.Why.ToPlainText();

        partial void OnActivePanelChanged(string value)
        {
            OnPropertyChanged(nameof(IsWhyOpen));
            OnPropertyChanged(nameof(IsEvidenceOpen));
            OnPropertyChanged(nameof(IsVerifyOpen));
            OnPropertyChanged(nameof(IsTodoOpen));
        }

        private void Toggle(string panel) => ActivePanel = ActivePanel == panel ? "" : panel;

        [RelayCommand] private void ShowWhy() => Toggle(PanelWhy);
        [RelayCommand] private void ShowEvidence() => Toggle(PanelEvidence);
        [RelayCommand] private void ShowVerify() => Toggle(PanelVerify);
        [RelayCommand] private void ShowTodo() => Toggle(PanelTodo);
    }
}
