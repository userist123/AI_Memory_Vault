using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class AiCopilotInvestigationEngine
    {
        public CopilotInvestigationResult Investigate(
            IEnumerable<ParsedEvent> events,
            IEnumerable<KerberosAdFinding> kerbFindings,
            IEnumerable<StandaloneSamFinding> samFindings,
            IEnumerable<UbaAnomalyItem> ubaAnomalies,
            bool isAirGapped)
        {
            var result = new CopilotInvestigationResult();
            var eventList = events?.ToList() ?? new List<ParsedEvent>();
            var kList = kerbFindings?.ToList() ?? new List<KerberosAdFinding>();
            var sList = samFindings?.ToList() ?? new List<StandaloneSamFinding>();
            var uList = ubaAnomalies?.ToList() ?? new List<UbaAnomalyItem>();

            int totalFindings = kList.Count + sList.Count + uList.Count;

            if (totalFindings == 0)
            {
                result.RiskLevel = "Low";
                result.Title = "Sistem Curat — Nicio Amenințare Detectată";
                result.ExecutiveSummaryRo = isAirGapped 
                    ? "Analiza forensică pe stația standalone nu a identificat anomalii pe baza SAM, dispozitive USB neautorizate sau alterări ale politicilor de audit."
                    : "Analiza Active Directory și identitate cloud nu a identificat atacuri Kerberos, modificări privileged neautorizate sau abateri de comportament UBA.";
                result.RecommendedContainmentSteps.Add("Mențineți politica de auditare activă și continuați monitorizarea periodică a jurnalelor.");
                result.RegulatoryImpactRo = "Conformitate deplină cu cerințele de securitate.";
                return result;
            }

            if (sList.Any(s => s.Severity == "Critical") || kList.Any(k => k.Severity == "Critical") || uList.Any(u => u.Severity == "Critical"))
            {
                result.RiskLevel = "Critic";
                result.Title = "AMENINȚARE CRITICĂ — Tentativă Avansată de Compromitere";
            }
            else if (sList.Any(s => s.Severity == "High") || kList.Any(k => k.Severity == "High") || uList.Any(u => u.Severity == "High"))
            {
                result.RiskLevel = "Ridicat";
                result.Title = "RISC RIDICAT — Activitate Suspectă / Abatere de Securitate";
            }
            else
            {
                result.RiskLevel = "Mediu";
                result.Title = "ATENȚIE — Anomalii Minore Detectate";
            }

            if (isAirGapped)
            {
                result.ExecutiveSummaryRo = $"Investigația pe stația izolată a identificat {sList.Count} constatări pe baza SAM/Endpoint și {uList.Count} deviații de sesiune. ";
                result.RegulatoryImpactRo = "Impact potențial asupra conformității cu HG 585/2002 privind protecția informațiilor clasificate.";
                if (sList.Any(s => s.FindingType.Contains("auditpol", StringComparison.OrdinalIgnoreCase)))
                {
                    result.ExecutiveSummaryRo += "A fost detectată alterarea politicilor locale de auditare (tentativă de mascare a activității). ";
                    result.RecommendedContainmentSteps.Add("Restaurați imediat politica de audit standard din baseline-ul securizat HG 585.");
                }
                if (sList.Any(s => s.FindingType.Contains("USB", StringComparison.OrdinalIgnoreCase)))
                {
                    result.ExecutiveSummaryRo += "Au fost detectate medii de stocare USB conectate la sistem. ";
                    result.RecommendedContainmentSteps.Add("Verificați seria hardware a stick-ului USB în Registrul de Medii de Stocare.");
                }
                if (sList.Any(s => s.FindingType.Contains("SeDebugPrivilege", StringComparison.OrdinalIgnoreCase)))
                {
                    result.RecommendedContainmentSteps.Add("Restricționați dreptul SeDebugPrivilege exclusiv pentru contul Local SYSTEM.");
                }
            }
            else
            {
                result.ExecutiveSummaryRo = $"Investigația Active Directory a corelat {kList.Count} detecții de atac domeniu, {uList.Count} anomalii UBA și {sList.Count} evenimente de endpoint. ";
                result.RegulatoryImpactRo = "Obligație de notificare timpurie în 24h conform Directivei NIS2 / OUG 155/2024 în caz de confirmare a incidentului major.";
                if (kList.Any(k => k.AttackType.Contains("Kerberoast", StringComparison.OrdinalIgnoreCase)))
                {
                    result.RecommendedContainmentSteps.Add("Resetați parolele conturilor de serviciu cu SPN-uri asociate și impuneți lungime minimă de 25 caractere cu criptare AES-256.");
                }
                if (kList.Any(k => k.AttackType.Contains("AS-REP", StringComparison.OrdinalIgnoreCase)))
                {
                    result.RecommendedContainmentSteps.Add("Reactivați pre-autentificarea Kerberos (eliminați flag-ul DONT_REQ_PREAUTH) pe toate conturile de utilizator.");
                }
                if (kList.Any(k => k.AttackType.Contains("DCSync", StringComparison.OrdinalIgnoreCase) || k.AttackType.Contains("DCShadow", StringComparison.OrdinalIgnoreCase)))
                {
                    result.RecommendedContainmentSteps.Add("Izolați de urgență stația sursă din rețea și inițiați procedura de resetare a parolei contului KRBTGT (dublă resetare).");
                }
            }

            return result;
        }

        public CopilotInvestigationResult InvestigateFinding(string findingTitle, string category, string targetAccount, string description, string mitreTechniqueId)
        {
            var res = new CopilotInvestigationResult
            {
                Title = $"Investigație Asistată: {findingTitle}",
                RiskLevel = "Critic",
                MitreKillChainMapping = mitreTechniqueId,
                ExecutiveSummaryRo = $"Analiza automată a corelat incidentul '{findingTitle}' (Categorie: {category}, Țintă: {targetAccount}). {description}",
                RegulatoryImpactRo = "Incident critic cu impact direct asupra conformității HG 585/2002 și Directiva NIS2."
            };

            res.ForensicEvidenceBullets.Add($"Entitate / Țintă afectată: {targetAccount}");
            res.ForensicEvidenceBullets.Add($"Clasificare MITRE: {mitreTechniqueId} ({category})");
            res.RecommendedContainmentSteps.Add("Izolați contul sau resursa din Active Directory / SAM.");
            res.RecommendedContainmentSteps.Add("Rotiți credențialele compromise și analizați jurnalele adiacente.");

            return res;
        }
    }
}
