using System.Text;
using LogAnalyzer.Dfir.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// "Cerere de înregistrare a unui ofițer de securitate": the one-page PDF an already registered officer signs with their qualified certificate to approve a
/// new officer. Besides the visible text it carries, as a PDF comment just before the final "startxref", the marker naming the new certificate's
/// fingerprint (<see cref="OfficerRegistry.RequestMarker"/>), so an approval cannot be reused for another certificate. Nothing before the marker moves,
/// so the cross-reference offsets stay valid.
/// </summary>
public static class OfficerRequestPdf
{
    public static void Write(string path, SecurityOfficerRow candidate, string station, DateTimeOffset nowUtc)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(Convert.FromBase64String(candidate.Certificate));
        var pdf = Document.Create(doc => { doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));
            page.Content().Column(c =>
            {
                c.Spacing(8);
                c.Item().Text("Cerere de înregistrare a unui ofițer de securitate").Bold().FontSize(15);
                c.Item().Text($"Stația: {station}. Data: {TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.Local):yyyy-MM-dd HH:mm} (ora stației).");
                c.Item().Text($"Ofițerul care se înregistrează: {candidate.Name}");
                c.Item().Text($"Certificatul calificat: {cert.Subject}; emis de {cert.Issuer}; valabil {cert.NotBefore:yyyy-MM-dd} – {cert.NotAfter:yyyy-MM-dd}; seria {cert.SerialNumber}.");
                c.Item().Text($"Amprenta certificatului (SHA-256): {candidate.Fingerprint}").Bold();
                c.Item().PaddingTop(10).Text("Semnând acest document cu certificatul meu calificat, eu, ofițer de securitate deja înregistrat pe această stație, aprob ca titularul certificatului de mai sus să poată semna rapoartele oficiale de control.");
            });
        });
        SignatureBoxPage.Add(doc, null, "Semnează un ofițer de securitate deja înregistrat, doar în acest câmp (în LogAnalyzer sau în Adobe, câmpul „Semnatura ofiterului de securitate”)."); }).GeneratePdf();
        File.WriteAllBytes(path, SignatureField.Reserve(InsertMarker(pdf, OfficerRegistry.RequestMarker(candidate.Fingerprint))));
        cert.Dispose();
    }

    /// <summary>Inserts "<paramref name="marker"/>\n" right before the last "startxref" (a PDF comment; the offsets of everything before it are unchanged).</summary>
    internal static byte[] InsertMarker(byte[] pdf, string marker)
    {
        var key = "startxref"u8;
        int at = pdf.AsSpan().LastIndexOf(key);
        if (at < 0) throw new InvalidOperationException("PDF fără startxref");
        var m = Encoding.ASCII.GetBytes(marker + "\n");
        return [.. pdf.AsSpan(0, at), .. m, .. pdf.AsSpan(at)];
    }
}
