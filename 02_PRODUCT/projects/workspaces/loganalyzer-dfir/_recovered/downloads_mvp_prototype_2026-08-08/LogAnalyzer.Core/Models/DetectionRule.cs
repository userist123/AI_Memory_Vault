using System.Collections.Generic;

namespace LogAnalyzer.Core.Models
{
    public enum StatusRegula { Activa, Dezactivata, Experimentala }
    public enum SeveritateRegula { Informativ, Low, Medium, High, Critical }
    public enum IncredereMitre { Low, Medium, High }

    public class MitreMapping
    {
        public string TacticaId { get; set; } = string.Empty;
        public string TacticaNume { get; set; } = string.Empty;
        public string TehnicaId { get; set; } = string.Empty;
        public string TehnicaNume { get; set; } = string.Empty;
        public IncredereMitre Incredere { get; set; } = IncredereMitre.Medium;
    }

    public class DetectionRule
    {
        public string Id { get; set; } = string.Empty;
        public string Titlu { get; set; } = string.Empty;
        public string Descriere { get; set; } = string.Empty;
        public StatusRegula Status { get; set; } = StatusRegula.Activa;
        public SeveritateRegula Severitate { get; set; } = SeveritateRegula.Medium;
        public List<string> SurseLog { get; set; } = new List<string>();
        public List<int> EventIduri { get; set; } = new List<int>();
        public int PragEvenimente { get; set; } = 1;
        public int FereastraMinute { get; set; } = 0;
        public string PatternPseudoSigma { get; set; } = string.Empty;
        public MitreMapping Mitre { get; set; } = new MitreMapping();
        public string Recomandare { get; set; } = string.Empty;
    }
}
