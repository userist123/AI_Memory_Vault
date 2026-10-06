using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LogAnalyzer.Core.Models
{
    public enum AlertStatus { Noua, InAnaliza, Escalata, Inchisa }
    public enum AlertResolution { Neclasificat, TruePositive, FalsePositive }

    public partial class DetectedIssue : ObservableObject
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string SuggestedScript { get; set; } = string.Empty;
        public List<ParsedEvent> RelatedEvents { get; set; } = new List<ParsedEvent>();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ObservableProperty] private AlertStatus _status = AlertStatus.Noua;
        [ObservableProperty] private AlertResolution _resolution = AlertResolution.Neclasificat;
        [ObservableProperty] private bool _isVerified;
        [ObservableProperty] private string _analystNotes = string.Empty;
        [ObservableProperty] private DateTime? _closedAt;

        public string MitreTacticId { get; set; } = string.Empty;
        public string MitreTacticName { get; set; } = string.Empty;
        public string MitreTechniqueId { get; set; } = string.Empty;
        public string MitreTechniqueName { get; set; } = string.Empty;
        public bool HasMitreMapping => !string.IsNullOrEmpty(MitreTechniqueId);

        partial void OnStatusChanged(AlertStatus value)
        {
            if (value == AlertStatus.Inchisa) ClosedAt = DateTime.Now;
        }
    }
}
