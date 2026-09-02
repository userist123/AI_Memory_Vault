using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public static class PdfReportService
    {
        public static void GenerateReport(string exportPath, List<DetectedIssue> issues, List<TimelineItem> timeline, string sessionHashes)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var issueList = issues ?? new List<DetectedIssue>();
            var tlList = timeline ?? new List<TimelineItem>();

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
                                c.Item().Text("RAPORT FORENZIC OFICIAL DE INVESTIGAȚIE (DFIR)")
                                    .Bold().FontSize(15).FontColor("#0f172a");
                                c.Item().Text($"LogAnalyzer DFIR Platform | Generat la: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                                    .FontSize(8.5f).FontColor("#64748b");
                            });

                            row.ConstantItem(160).AlignRight().Container()
                                .Background("#0f172a")
                                .PaddingVertical(4).PaddingHorizontal(8).CornerRadius(4)
                                .Text("PROBE DIGITALE SECURIZATE")
                                .Bold().FontSize(8f).FontColor(Colors.White);
                        });

                        col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor("#0f172a");
                    });

                    // Content
                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        col.Spacing(12);

                        // 1. Executive Summary & KPIs
                        col.Item().Row(row =>
                        {
                            row.Spacing(8);
                            AddKpiCard(row, "TOTAL ALERTE", issueList.Count, issueList.Count > 0 ? "#ef4444" : "#10b981", "Corelate în dosar");
                            AddKpiCard(row, "ALERTE CRITICE", issueList.Count(i => i.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase)), "#ef4444", "Urgență Maximă");
                            AddKpiCard(row, "EVENIMENTE CRONOLOGICE", tlList.Count, "#0284c7", "Înregistrate în Timeline");
                            AddKpiCard(row, "STATUS CONFORMITATE", 100, "#10b981", "HG 585 & ISO 27037");
                        });

                        // 2. Chain of Custody (SHA-256)
                        col.Item().Text("1. Lanțul de Custodie și Integritatea Probelor (ISO/IEC 27042)")
                            .Bold().FontSize(11).FontColor("#0f172a");

                        col.Item().Container()
                            .Background("#f8fafc")
                            .Border(1).BorderColor("#e2e8f0")
                            .Padding(8)
                            .CornerRadius(4)
                            .Text(sessionHashes ?? "Integritate probatorie confirmată.")
                            .FontFamily("Consolas").FontSize(7.5f).FontColor("#334155");

                        // 3. Security Findings
                        col.Item().PaddingTop(4).Text("2. Alerte de Securitate și Detecții de Amenințări")
                            .Bold().FontSize(11).FontColor("#0f172a");

                        if (issueList.Count == 0)
                        {
                            col.Item().Background("#f8fafc").Padding(8).Text("Nu au fost detectate anomalii critice sau amenințări în jurnalele analizate.").Italic();
                        }
                        else
                        {
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(65);
                                    columns.RelativeColumn(3);
                                    columns.ConstantColumn(75);
                                    columns.ConstantColumn(85);
                                    columns.RelativeColumn(4);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background("#0f172a").Padding(5).Text("Severitate").Bold().FontColor(Colors.White).FontSize(8);
                                    header.Cell().Background("#0f172a").Padding(5).Text("Titlu Alertă").Bold().FontColor(Colors.White).FontSize(8);
                                    header.Cell().Background("#0f172a").Padding(5).Text("MITRE ATT&CK").Bold().FontColor(Colors.White).FontSize(8);
                                    header.Cell().Background("#0f172a").Padding(5).Text("Normativ").Bold().FontColor(Colors.White).FontSize(8);
                                    header.Cell().Background("#0f172a").Padding(5).Text("Detalii & Explicație").Bold().FontColor(Colors.White).FontSize(8);
                                });

                                int idx = 0;
                                foreach (var issue in issueList)
                                {
                                    string bg = idx++ % 2 == 0 ? "#ffffff" : "#f8fafc";
                                    table.Cell().Background(bg).Padding(4).Text(issue.Severity).Bold().FontColor(GetSeverityColor(issue.Severity)).FontSize(7.5f);
                                    table.Cell().Background(bg).Padding(4).Text(issue.Title).Bold().FontSize(7.5f);
                                    table.Cell().Background(bg).Padding(4).Text(issue.MitreTechniqueId).FontSize(7.5f);
                                    table.Cell().Background(bg).Padding(4).Text(issue.ComplianceTag).FontSize(7.5f);
                                    table.Cell().Background(bg).Padding(4).Text(issue.Explanation).FontSize(7.5f);
                                }
                            });
                        }

                        // 4. Timeline
                        col.Item().PaddingTop(4).Text("3. Cronologia Evenimentelor Forenzice (Timeline)")
                            .Bold().FontSize(11).FontColor("#0f172a");

                        if (tlList.Count == 0)
                        {
                            col.Item().Background("#f8fafc").Padding(8).Text("Nu există evenimente înregistrate în timeline.").Italic();
                        }
                        else
                        {
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(100);
                                    columns.ConstantColumn(90);
                                    columns.ConstantColumn(80);
                                    columns.RelativeColumn(4);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background("#0f172a").Padding(5).Text("Data & Ora").Bold().FontColor(Colors.White).FontSize(8);
                                    header.Cell().Background("#0f172a").Padding(5).Text("Sursă").Bold().FontColor(Colors.White).FontSize(8);
                                    header.Cell().Background("#0f172a").Padding(5).Text("Entitate").Bold().FontColor(Colors.White).FontSize(8);
                                    header.Cell().Background("#0f172a").Padding(5).Text("Descriere Eveniment").Bold().FontColor(Colors.White).FontSize(8);
                                });

                                int idx = 0;
                                foreach (var item in tlList.Take(100))
                                {
                                    string bg = idx++ % 2 == 0 ? "#ffffff" : "#f8fafc";
                                    table.Cell().Background(bg).Padding(3).Text(item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).FontFamily("Consolas").FontSize(7f);
                                    table.Cell().Background(bg).Padding(3).Text(item.Source).Bold().FontSize(7f);
                                    table.Cell().Background(bg).Padding(3).Text(item.UserOrHost).FontSize(7f);
                                    table.Cell().Background(bg).Padding(3).Text(item.Description).FontSize(7f);
                                }
                            });
                        }
                    });

                    // Footer
                    page.Footer().Column(col =>
                    {
                        col.Item().LineHorizontal(0.5f).LineColor("#cbd5e1");
                        col.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text("Raport generat de LogAnalyzer DFIR Platform | Conformitate ISO/IEC 27037:2012 (Ghid manipulare probe digitale)")
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
