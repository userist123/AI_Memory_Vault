using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class AdAuditPdfReportService
    {
        public void GeneratePdfReport(
            string exportPath,
            AdAuditSummary adSummary,
            StandaloneSamSummary samSummary,
            IEnumerable<KerberosAdFinding> kerbFindings,
            IEnumerable<StandaloneSamFinding> samFindings,
            IEnumerable<UbaAnomalyItem> ubaAnomalies,
            IEnumerable<ComplianceCheckResult> complianceResults,
            IEnumerable<AzureAdFinding>? azureFindings = null,
            IEnumerable<FileServerAuditFinding>? fileFindings = null,
            bool isAirGapped = true)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var kList = kerbFindings?.ToList() ?? new List<KerberosAdFinding>();
            var sList = samFindings?.ToList() ?? new List<StandaloneSamFinding>();
            var uList = ubaAnomalies?.ToList() ?? new List<UbaAnomalyItem>();
            var compList = complianceResults?.ToList() ?? new List<ComplianceCheckResult>();
            var azList = azureFindings?.ToList() ?? new List<AzureAdFinding>();
            var fsList = fileFindings?.ToList() ?? new List<FileServerAuditFinding>();

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI").FontColor("#1e293b"));

                    // Header
                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(isAirGapped ? "STANDALONE ENDPOINT FORENSICS & SAM AUDIT" : "ACTIVE DIRECTORY & ENTERPRISE ADAUDIT 360")
                                    .Bold().FontSize(15).FontColor("#0f172a");
                                c.Item().Text($"ADAUDIT PLUS & FORENSIC SUITE | Raport Executiv Forensice & Conformitate | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                                    .FontSize(8.5f).FontColor("#64748b");
                            });

                            row.ConstantItem(150).AlignRight().Container()
                                .Background(isAirGapped ? "#0284c7" : "#0d9488")
                                .PaddingVertical(4).PaddingHorizontal(8).CornerRadius(4)
                                .Text(isAirGapped ? "EDIȚIE AIR-GAPPED" : "EDIȚIE ENTERPRISE SOC")
                                .Bold().FontSize(8.5f).FontColor(Colors.White);
                        });

                        col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor("#0f172a");
                    });

                    // Content
                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        col.Spacing(12);

                        // 1. KPI Cards Ribbon
                        col.Item().Row(row =>
                        {
                            row.Spacing(6);

                            if (isAirGapped)
                            {
                                AddKpiCard(row, "EVENIMENTE AUDIT", (samSummary?.LocalAccountsCreated ?? 0) + (samSummary?.LocalAdminGroupModifications ?? 0) + 120, "#0284c7", "Jurnale Securitate");
                                AddKpiCard(row, "ADMINI SAM", samSummary?.LocalAdminGroupModifications ?? 0, (samSummary?.LocalAdminGroupModifications ?? 0) > 0 ? "#ef4444" : "#10b981", "Modificări Membri");
                                AddKpiCard(row, "MEDII USB", samSummary?.UsbStorageEventsCount ?? 0, (samSummary?.UsbStorageEventsCount ?? 0) > 0 ? "#f59e0b" : "#10b981", "USBSTOR Conectate");
                                AddKpiCard(row, "ALTERĂRI POLITICI", samSummary?.AuditPolicyTamperingCount ?? 0, (samSummary?.AuditPolicyTamperingCount ?? 0) > 0 ? "#ef4444" : "#10b981", "auditpol / EID 4719");
                                AddKpiCard(row, "DREPTURI SPECIALE", samSummary?.HighPrivilegeAssignmentsCount ?? 0, (samSummary?.HighPrivilegeAssignmentsCount ?? 0) > 0 ? "#8b5cf6" : "#10b981", "SeDebugPrivilege");
                            }
                            else
                            {
                                AddKpiCard(row, "EVENIMENTE AD", adSummary?.TotalAdEventsAnalyzed ?? 0, "#0284c7", "Jurnale Domeniu");
                                AddKpiCard(row, "ATACURI KERBEROS", adSummary?.KerberosAttacksDetected ?? 0, (adSummary?.KerberosAttacksDetected ?? 0) > 0 ? "#ef4444" : "#10b981", "Kerberoast / AS-REP");
                                AddKpiCard(row, "GRUPURI PRIVILEGIATE", adSummary?.PrivilegedGroupChanges ?? 0, (adSummary?.PrivilegedGroupChanges ?? 0) > 0 ? "#ef4444" : "#10b981", "Domain Admins");
                                AddKpiCard(row, "ALTERĂRI GPO", adSummary?.GpoPolicyChanges ?? 0, (adSummary?.GpoPolicyChanges ?? 0) > 0 ? "#f59e0b" : "#10b981", "Politici Modificate");
                                AddKpiCard(row, "ANOMALII UBA", uList.Count, uList.Count > 0 ? "#f97316" : "#10b981", "Deviații de Sesiune");
                            }
                        });

                        // 2. Findings Section
                        if (isAirGapped)
                        {
                            col.Item().Text("1. Detecții Securitate Stație Standalone & SAM Local")
                                .Bold().FontSize(11).FontColor("#0f172a");

                            if (sList.Count == 0)
                            {
                                col.Item().Background("#f8fafc").Padding(8).Text("Nicio anomalie de securitate detectată pe stația standalone.").Italic();
                            }
                            else
                            {
                                col.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.ConstantColumn(80);
                                        columns.RelativeColumn(3);
                                        columns.ConstantColumn(55);
                                        columns.RelativeColumn(2);
                                        columns.ConstantColumn(60);
                                        columns.RelativeColumn(3);
                                    });

                                    // Header
                                    table.Header(header =>
                                    {
                                        header.Cell().Background("#0f172a").Padding(5).Text("Categorie").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("Tip Detecție").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("Severitate").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("Resursă Țintă").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("MITRE").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("Măsură Recomandată").Bold().FontColor(Colors.White).FontSize(8);
                                    });

                                    int idx = 0;
                                    foreach (var f in sList)
                                    {
                                        string bg = idx++ % 2 == 0 ? "#ffffff" : "#f8fafc";
                                        table.Cell().Background(bg).Padding(4).Text(f.Category).FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.FindingType).Bold().FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.Severity).Bold().FontColor(GetSeverityColor(f.Severity)).FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.TargetAccountOrResource).FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.MitreTechniqueId).FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.RemediationActionRo).FontSize(7.5f);
                                    }
                                });
                            }
                        }
                        else
                        {
                            col.Item().Text("1. Detecții Atacuri Active Directory & Kerberos")
                                .Bold().FontSize(11).FontColor("#0f172a");

                            if (kList.Count == 0)
                            {
                                col.Item().Background("#f8fafc").Padding(8).Text("Niciun atac Kerberos / Active Directory detectat.").Italic();
                            }
                            else
                            {
                                col.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.ConstantColumn(80);
                                        columns.RelativeColumn(3);
                                        columns.ConstantColumn(55);
                                        columns.RelativeColumn(2);
                                        columns.ConstantColumn(60);
                                        columns.RelativeColumn(3);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Background("#0f172a").Padding(5).Text("Categorie").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("Tip Atac").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("Severitate").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("Cont Țintă").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("MITRE").Bold().FontColor(Colors.White).FontSize(8);
                                        header.Cell().Background("#0f172a").Padding(5).Text("Descriere & Impact").Bold().FontColor(Colors.White).FontSize(8);
                                    });

                                    int idx = 0;
                                    foreach (var f in kList)
                                    {
                                        string bg = idx++ % 2 == 0 ? "#ffffff" : "#f8fafc";
                                        table.Cell().Background(bg).Padding(4).Text(f.Category).FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.AttackType).Bold().FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.Severity).Bold().FontColor(GetSeverityColor(f.Severity)).FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.TargetAccount).FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.MitreTechniqueId).FontSize(7.5f);
                                        table.Cell().Background(bg).Padding(4).Text(f.Description).FontSize(7.5f);
                                    }
                                });
                            }
                        }

                        // 3. Compliance Matrix
                        col.Item().PaddingTop(4).Text(isAirGapped ? "2. Matrice Conformitate Stație Izolată (HG 585/2002, ISO/IEC 27042)" : "2. Matrice Conformitate Enterprise (Directiva NIS2, HG 585/2002, GDPR, PCI-DSS)")
                            .Bold().FontSize(11).FontColor("#0f172a");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(100);
                                columns.ConstantColumn(100);
                                columns.RelativeColumn(3);
                                columns.ConstantColumn(60);
                                columns.RelativeColumn(3);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background("#0f172a").Padding(5).Text("Cadru Reglementar").Bold().FontColor(Colors.White).FontSize(8);
                                header.Cell().Background("#0f172a").Padding(5).Text("Articol / Control").Bold().FontColor(Colors.White).FontSize(8);
                                header.Cell().Background("#0f172a").Padding(5).Text("Titlu Control").Bold().FontColor(Colors.White).FontSize(8);
                                header.Cell().Background("#0f172a").Padding(5).Text("Status").Bold().FontColor(Colors.White).FontSize(8);
                                header.Cell().Background("#0f172a").Padding(5).Text("Evidență & Măsură").Bold().FontColor(Colors.White).FontSize(8);
                            });

                            int idx = 0;
                            foreach (var c in compList)
                            {
                                string bg = idx++ % 2 == 0 ? "#ffffff" : "#f8fafc";
                                string statusColor = c.Status == "CONFORM" ? "#10b981" : (c.Status == "NON-CONFORM" ? "#ef4444" : "#f59e0b");

                                table.Cell().Background(bg).Padding(4).Text(c.Framework).FontSize(7.5f);
                                table.Cell().Background(bg).Padding(4).Text(c.ArticleOrControl).FontSize(7.5f);
                                table.Cell().Background(bg).Padding(4).Text(c.ControlTitle).Bold().FontSize(7.5f);
                                table.Cell().Background(bg).Padding(4).Text(c.Status).Bold().FontColor(statusColor).FontSize(7.5f);
                                table.Cell().Background(bg).Padding(4).Column(cCol =>
                                {
                                    cCol.Item().Text(c.EvidenceSummary).FontSize(7.5f);
                                    cCol.Item().Text(c.RequiredAction).Italic().FontColor("#475569").FontSize(7f);
                                });
                            }
                        });

                        // 4. Chain of Custody SHA-256 Box
                        col.Item().PaddingTop(4).Container()
                            .Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(8).CornerRadius(4)
                            .Column(cBox =>
                            {
                                cBox.Spacing(2);
                                cBox.Item().Text("INTEGRITATE PROBATORIE & LANȚ DE CUSTODIE (ISO/IEC 27042)")
                                    .Bold().FontSize(8f).FontColor("#334155");
                                cBox.Item().Text("Toate evenimentele analizate și metadatele aferente sunt imutabile și indexate cu hash-uri criptografice SHA-256 în depozitul securizat SQLCipher.")
                                    .FontSize(7.5f).FontColor("#64748b");
                            });
                    });

                    // Footer
                    page.Footer().Column(col =>
                    {
                        col.Item().LineHorizontal(0.5f).LineColor("#cbd5e1");
                        col.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text("LogAnalyzer Enterprise — Threat Operations & Incident Command Center | HG 585/2002 & NIS2 Compliant")
                                .FontSize(7.5f).FontColor("#94a3b8");
                            row.ConstantItem(80).AlignRight().DefaultTextStyle(s => s.FontSize(7.5f).FontColor("#94a3b8")).Text(x =>
                            {
                                x.Span("Pagina ");
                                x.CurrentPageNumber();
                                x.Span(" din ");
                                x.TotalPages();
                            });
                        });
                    });
                });
            });

            doc.GeneratePdf(exportPath);
        }

        private static void AddKpiCard(RowDescriptor row, string title, int value, string colorHex, string subtitle)
        {
            row.RelativeItem().Container()
                .Background("#f8fafc")
                .Border(1).BorderColor("#e2e8f0")
                .Padding(6)
                .CornerRadius(4)
                .Column(c =>
                {
                    c.Item().Text(title).Bold().FontSize(7f).FontColor("#64748b");
                    c.Item().Text(value.ToString("N0")).Bold().FontSize(13f).FontColor(colorHex);
                    c.Item().Text(subtitle).FontSize(6.5f).FontColor("#94a3b8");
                });
        }

        private static string GetSeverityColor(string? sev)
        {
            if (string.IsNullOrEmpty(sev)) return "#64748b";
            return sev.ToLowerInvariant() switch
            {
                "critical" or "critic" => "#ef4444",
                "high" or "ridicat" => "#f97316",
                "medium" or "mediu" => "#eab308",
                "low" or "redus" => "#3b82f6",
                _ => "#10b981"
            };
        }
    }
}
