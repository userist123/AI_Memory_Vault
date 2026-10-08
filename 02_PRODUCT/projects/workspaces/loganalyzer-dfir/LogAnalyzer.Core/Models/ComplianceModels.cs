using System;

namespace LogAnalyzer.Core.Models
{
    /// <summary>Status vocabulary of <see cref="ComplianceCheckResult"/>. NEEVALUAT = nothing in the supplied data allows a verdict; it is not a pass.</summary>
    public static class ComplianceStatus
    {
        public const string Conform = "CONFORM";
        public const string NonConform = "NON-CONFORM";
        public const string Attention = "ATENȚIE";
        public const string NotAssessed = "NEEVALUAT";
    }

    /// <summary>
    /// Rezultat evaluare automată a conformității pe cadre legale (HG 585/2002, NIS2, ISO 27042, GDPR, PCI-DSS).
    /// </summary>
    public class ComplianceCheckResult
    {
        public string Framework { get; set; } = string.Empty;
        public string ArticleOrControl { get; set; } = string.Empty;
        public string ControlTitle { get; set; } = string.Empty;
        /// <summary>One of <see cref="ComplianceStatus"/>. Defaults to not assessed: a result is never CONFORM unless something set it from evidence.</summary>
        public string Status { get; set; } = ComplianceStatus.NotAssessed;
        public string EvidenceSummary { get; set; } = string.Empty;
        public string RequiredAction { get; set; } = string.Empty;
    }

    /// <summary>
    /// Risc identificat în structura de fișiere, ACL-uri sau permisiuni de stocare.
    /// </summary>
    public class StorageAuditItem
    {
        public string ResourcePath { get; set; } = string.Empty;
        public string RiskCategory { get; set; } = "Excessive Permissions";
        public string Details { get; set; } = string.Empty;
        public string Severity { get; set; } = "Medium";
        public string StorageImpact { get; set; } = "Reclaimable / Security Risk";
        public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    }
}
