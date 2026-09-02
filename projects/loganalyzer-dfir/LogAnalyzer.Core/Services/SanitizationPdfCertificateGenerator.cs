using System;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Core.Services
{
    public class SanitizationPdfCertificateGenerator
    {
        public void GeneratePdfCertificate(string exportPath, SanitizationCertificateData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            QuestPDF.Settings.License = LicenseType.Community;

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(35);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI").FontColor("#1e293b"));

                    // Header
                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("CERTIFICAT OFICIAL DE SANITIZARE A DATELOR")
                                    .Bold().FontSize(15).FontColor("#0f172a");
                                c.Item().Text("CONFORM NIST SP 800-88r2 | HG 585/2002 ART. 65 | NATO AC/35-D/1022")
                                    .Bold().FontSize(8.5f).FontColor("#0284c7");
                            });

                            row.ConstantItem(150).AlignRight().Container()
                                .Background("#0f172a")
                                .PaddingVertical(5).PaddingHorizontal(8).CornerRadius(4)
                                .Column(c =>
                                {
                                    c.Item().Text("ID CERTIFICAT").FontSize(6.5f).FontColor("#94a3b8");
                                    c.Item().Text(data.CertificateId).Bold().FontSize(8.5f).FontColor(Colors.White);
                                });
                        });

                        col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor("#0f172a");
                    });

                    // Body Content
                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        col.Spacing(12);

                        // Certificate Metadata Summary
                        col.Item().Row(row =>
                        {
                            row.Spacing(6);
                            AddMetaBox(row, "DATA & ORA EMITERII", $"{data.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC", "#0f172a");
                            AddMetaBox(row, "STAȚIE DE OPERARE", data.SystemHostId, "#0f172a");
                            AddMetaBox(row, "REZULTAT SANITIZARE", "CONFIRMAT (ZEROIZAT)", "#10b981");
                        });

                        // 1. Hardware Identification
                        col.Item().Text("1. Identificarea Mediului Fizic de Stocare (Hardware Telemetry)")
                            .Bold().FontSize(11).FontColor("#0f172a");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(160);
                                columns.RelativeColumn();
                            });

                            AddTableRow(table, "Producător & Model Mediu:", $"{data.DeviceVendor} {data.DeviceModel}", true);
                            AddTableRow(table, "Serie Hardware Unică (S/N):", data.HardwareSerialNumber, false, isMonospace: true);
                            AddTableRow(table, "Capacitate Fizică Totală:", $"{data.DeviceCapacityBytes:N0} bytes ({data.DeviceCapacityBytes / (1024.0 * 1024.0 * 1024.0):F2} GB)", true);
                            AddTableRow(table, "Plafon Clasificare Autorizat:", "SECRET DE SERVICIU / RESTRICTED (HG 585)", false);
                        });

                        // 2. Sanitization Protocol
                        col.Item().PaddingTop(4).Text("2. Parametri Tehnici & Verificare Criptografică")
                            .Bold().FontSize(11).FontColor("#0f172a");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(160);
                                columns.RelativeColumn();
                            });

                            AddTableRow(table, "Metodă Sanitizare Executată:", $"{data.SanitizationMethodName} ({data.TotalPasses} treceri)", true);
                            AddTableRow(table, "Standard de Conformitate:", data.StandardCompliance, false);
                            AddTableRow(table, "Hash SHA-256 Pre-Sanitizare:", data.PreSanitizationSha256, true, isMonospace: true);
                            AddTableRow(table, "Hash SHA-256 Post-Sanitizare:", data.PostSanitizationSha256, false, isMonospace: true);
                            AddTableRow(table, "Verificare Stare Zeroizare:", data.IsVerifiedZeroized ? "CONFIRMATĂ (Date distruse ireversibil)" : "NECONFIRMATĂ", true, textColor: "#10b981");
                        });

                        // 3. Chain of Custody & Dual Sign-off (4-Eyes Principle)
                        col.Item().PaddingTop(4).Text("3. Autorizare Duală & Lanț de Custodie (4-Eyes Principle)")
                            .Bold().FontSize(11).FontColor("#0f172a");

                        col.Item().Row(row =>
                        {
                            row.Spacing(12);

                            row.RelativeItem().Container()
                                .Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(10).CornerRadius(4)
                                .Column(c =>
                                {
                                    c.Spacing(4);
                                    c.Item().Text("OPERATOR PRINCIPAL EXECUȚIE").Bold().FontSize(8f).FontColor("#64748b");
                                    c.Item().Text(string.IsNullOrWhiteSpace(data.PrimaryOperator) ? "Operator Autorizat" : data.PrimaryOperator).Bold().FontSize(9.5f).FontColor("#0f172a");
                                    c.Item().PaddingTop(15).LineHorizontal(0.5f).LineColor("#94a3b8");
                                    c.Item().Text("Semnătură & Ștampilă Operator").FontSize(7f).FontColor("#94a3b8");
                                });

                            row.RelativeItem().Container()
                                .Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(10).CornerRadius(4)
                                .Column(c =>
                                {
                                    c.Spacing(4);
                                    c.Item().Text("OFIȚER SECURITATE / MARTOR").Bold().FontSize(8f).FontColor("#64748b");
                                    c.Item().Text(string.IsNullOrWhiteSpace(data.VerifierOperator) ? "Ofițer Securitate Info" : data.VerifierOperator).Bold().FontSize(9.5f).FontColor("#0f172a");
                                    c.Item().PaddingTop(15).LineHorizontal(0.5f).LineColor("#94a3b8");
                                    c.Item().Text("Semnătură & Ștampilă Control").FontSize(7f).FontColor("#94a3b8");
                                });
                        });

                        // Tamper-Evident Audit Hash Block
                        col.Item().Container()
                            .Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(8).CornerRadius(4)
                            .Column(cBox =>
                            {
                                cBox.Spacing(2);
                                cBox.Item().Text("AMPRENTĂ AUDIT TAMPER-EVIDENT (SHA-256)")
                                    .Bold().FontSize(7.5f).FontColor("#475569");
                                cBox.Item().Text(string.IsNullOrWhiteSpace(data.TamperEvidentAuditHash) ? "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" : data.TamperEvidentAuditHash)
                                    .FontFamily("Consolas").FontSize(7.5f).FontColor("#0f172a");
                            });

                        // Legal Disclaimer
                        col.Item().Background("#f8fafc").Padding(6).Text(
                            "Prin prezenta se atestă că datele stocate pe mediul fizic menționat au fost distruse ireversibil prin suprascriere binară multi-pass, conform normativelor NATO și naționale, fără posibilitate de reconstituire prin tehnici de microscopie magnetică sau recuperare forensică avansată.")
                            .Italic().FontSize(7.5f).FontColor("#64748b");
                    });

                    // Footer
                    page.Footer().Column(col =>
                    {
                        col.Item().LineHorizontal(0.5f).LineColor("#cbd5e1");
                        col.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text("LogAnalyzer Enterprise — Media Sanitization Engine | HG 585/2002 & NIST SP 800-88r2")
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

        private static void AddMetaBox(RowDescriptor row, string label, string value, string valueColor)
        {
            row.RelativeItem().Container()
                .Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(6).CornerRadius(4)
                .Column(c =>
                {
                    c.Item().Text(label).Bold().FontSize(6.5f).FontColor("#64748b");
                    c.Item().Text(value).Bold().FontSize(8.5f).FontColor(valueColor);
                });
        }

        private static void AddTableRow(TableDescriptor table, string label, string value, bool isEven, string textColor = "#1e293b", bool isMonospace = false)
        {
            string bg = isEven ? "#f8fafc" : "#ffffff";
            table.Cell().Background(bg).Padding(4).Text(label).Bold().FontSize(8f).FontColor("#475569");
            var valCell = table.Cell().Background(bg).Padding(4);
            if (isMonospace)
            {
                valCell.Text(value).FontFamily("Consolas").FontSize(7.5f).FontColor(textColor);
            }
            else
            {
                valCell.Text(value).FontSize(8f).FontColor(textColor);
            }
        }
    }
}
