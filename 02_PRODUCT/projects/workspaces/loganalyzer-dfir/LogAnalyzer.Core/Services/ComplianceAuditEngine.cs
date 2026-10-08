using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    /// <summary>
    /// Legacy compliance matrix over the summaries of the SQLCipher/ParsedEvent path.
    /// It cannot prove conformity: the absence of events in a log is not evidence that a control is met
    /// (the log may not cover it). So a control is NON-CONFORM / ATENȚIE only when the summaries show something,
    /// and otherwise NEEVALUAT (not assessed). This engine never returns CONFORM.
    /// The formal control audit with evidence is the station/domain control audit (ControlStatus), not this matrix.
    /// </summary>
    public class ComplianceAuditEngine
    {
        private const string NoFindingNote = ". Nu s-a observat nimic în datele furnizate; absența evenimentelor nu dovedește conformitatea.";
        private const string NotAssessedAction = "Control neevaluat: datele furnizate nu permit un verdict. Folosiți auditul de control cu dovezi (stație / domeniu).";

        public List<ComplianceCheckResult> Evaluate(
            IEnumerable<ParsedEvent> events,
            AdAuditSummary adSummary,
            StandaloneSamSummary samSummary,
            int yaraCount,
            int anomalyCount)
        {
            var results = new List<ComplianceCheckResult>();

            // 1. HG 585/2002 - Art. 21 (Control Acces & Privilegii)
            int adminChanges = (adSummary?.PrivilegedGroupChanges ?? 0) + (samSummary?.LocalAdminGroupModifications ?? 0);
            int policyTamper = (adSummary?.GpoPolicyChanges ?? 0) + (samSummary?.AuditPolicyTamperingCount ?? 0);
            bool hg585Finding = adminChanges != 0 || policyTamper != 0;

            results.Add(new ComplianceCheckResult
            {
                Framework = "HG 585/2002 (România)",
                ArticleOrControl = "Art. 21 / Control Acces Privilegii",
                ControlTitle = "Gestiunea și Auditarea Rolurilor Administrative",
                Status = hg585Finding ? ComplianceStatus.NonConform : ComplianceStatus.NotAssessed,
                EvidenceSummary = $"Modificări Admini AD: {adSummary?.PrivilegedGroupChanges ?? 0}, Modificări Admini SAM: {samSummary?.LocalAdminGroupModifications ?? 0}, Alterări Politici: {policyTamper}" + (hg585Finding ? "" : NoFindingNote),
                RequiredAction = hg585Finding ? "Revizuirea imediată a numirilor în grupurile administrative și raportarea incidentului către Ofițerul de Securitate." : NotAssessedAction
            });

            // 2. Directiva NIS2 (UE 2022/2555) - Art. 21 (Incident Response & Lanț de Aprovizionare)
            int criticalThreats = (adSummary?.KerberosAttacksDetected ?? 0) + yaraCount;
            results.Add(new ComplianceCheckResult
            {
                Framework = "Directiva NIS2 (UE 2022/2555)",
                ArticleOrControl = "Art. 21 / Securitatea Lanțului & Incident Response",
                ControlTitle = "Capabilități de Detecție și Răspuns la Atacuri Avansate",
                Status = criticalThreats > 1 ? ComplianceStatus.NonConform : ComplianceStatus.NotAssessed,
                EvidenceSummary = $"Atacuri Kerberos / AD: {adSummary?.KerberosAttacksDetected ?? 0}, Semnături YARA Malicioase: {yaraCount}" + (criticalThreats > 1 ? "" : criticalThreats == 1 ? ". O detecție observată, sub pragul de neconformitate; nu este o evaluare de conformitate." : NoFindingNote),
                RequiredAction = criticalThreats > 1 ? "Inițiați raportarea timpurie de 24h conform OUG 155/2024 / mecanismului CSIRT." : NotAssessedAction
            });

            // 3. ISO/IEC 27042 - Clauza 7.4 (Integritatea Lanțului de Custodie)
            results.Add(new ComplianceCheckResult
            {
                Framework = "ISO/IEC 27042",
                ArticleOrControl = "Clauza 7.4 / Integritatea Lanțului de Custodie",
                ControlTitle = "Păstrarea Integrității Probatorii cu Hash Criptografic SHA-256",
                Status = ComplianceStatus.NotAssessed,
                EvidenceSummary = "Acest modul nu verifică integritatea probelor. Integritatea se verifică în fluxul de investigație (SHA-256 înainte și după parsare) și în lanțul de custodie al cazului.",
                RequiredAction = "Verificați integritatea din pagina de investigație sau din Lanțul de custodie; nu se emite aici o concluzie."
            });

            // 4. GDPR (UE 2016/679) - Art. 32 (Securitatea Prelucrării)
            int usbCount = samSummary?.UsbStorageEventsCount ?? 0;
            results.Add(new ComplianceCheckResult
            {
                Framework = "GDPR (UE 2016/679)",
                ArticleOrControl = "Art. 32 / Securitatea Prelucrării Datelor",
                ControlTitle = "Protecția Împotriva Scurgerilor și Extragerii Neautorizate",
                Status = usbCount > 0 ? ComplianceStatus.Attention : ComplianceStatus.NotAssessed,
                EvidenceSummary = $"Evenimente Stocare USB Removabilă: {usbCount}, Anomalii Comportamentale: {anomalyCount}" + (usbCount > 0 ? "" : NoFindingNote),
                RequiredAction = usbCount > 0 ? "Auditarea registrelor de transfer de date pe suporturi USB și verificarea autorizării purtătorului." : NotAssessedAction
            });

            // 5. PCI-DSS v4.0 - Cerința 8.3 & 10.2
            results.Add(new ComplianceCheckResult
            {
                Framework = "PCI-DSS v4.0",
                ArticleOrControl = "Cerința 8.3 & 10.2 / Audit Log & Autentificare",
                ControlTitle = "Protecția Mecanismelor de Autentificare și Contorizare Blocări",
                Status = ComplianceStatus.NotAssessed,
                EvidenceSummary = $"Blocări de Conturi (EID 4740): {adSummary?.AccountLockouts ?? 0}, Resetări Parole (EID 4724): {adSummary?.PasswordResets ?? 0}. Numărătorile nu constituie o evaluare a cerinței 8.3 / 10.2.",
                RequiredAction = NotAssessedAction
            });

            return results;
        }

        public List<ComplianceCheckResult> EvaluateCompliance(
            AdAuditSummary adSummary,
            StandaloneSamSummary samSummary,
            IEnumerable<KerberosAdFinding> kerbFindings,
            IEnumerable<StandaloneSamFinding> samFindings,
            IEnumerable<StorageAuditItem> storageItems)
        {
            return Evaluate(null, adSummary, samSummary, 0, (kerbFindings?.Count() ?? 0) + (samFindings?.Count() ?? 0));
        }
    }
}
