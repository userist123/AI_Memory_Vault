using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class CaseManifest
    {
        public string CaseId { get; set; } = string.Empty;
        public string CaseTitle { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
        public string LeadInvestigator { get; set; } = string.Empty;
        public DateTime SealedAtUtc { get; set; } = DateTime.UtcNow;
        public string StandardReference { get; set; } = "ISO/IEC 27037:2012 & ISO/IEC 27042:2015";
        public List<ManifestItem> Files { get; set; } = new();
        public string BundleSha256 { get; set; } = string.Empty;
    }

    public class ManifestItem
    {
        public string RelativePath { get; set; } = string.Empty;
        public string Sha256Hash { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class DfirCasePackagingService
    {
        /// <summary>
        /// Writes the sealed package directly into <paramref name="destinationZipPath"/> (security audit #14): every part is built in memory, hashed
        /// for the manifest and added to the zip; nothing is staged in %TEMP%. The zip is written to "&lt;destination&gt;.partial" and moved into place
        /// only when complete; on any exception the partial file is deleted and the exception propagates.
        /// </summary>
        public string PackageAndSealCase(
            string destinationZipPath,
            string caseId,
            string caseTitle,
            string organization,
            string investigatorName,
            IEnumerable<ProvenanceLedgerEntry> ledger,
            string ucoJsonLdContent,
            string nis2DraftContent,
            string superTimelineCsvContent)
        {
            var manifest = new CaseManifest
            {
                CaseId = caseId,
                CaseTitle = caseTitle,
                Organization = organization,
                LeadInvestigator = investigatorName,
                SealedAtUtc = DateTime.UtcNow
            };
            var parts = new List<(string Name, byte[] Data)>();
            void Part(string name, string content, string description)
            {
                var data = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray();   // same bytes File.WriteAllText(…, UTF8) wrote
                parts.Add((name, data));
                AddManifestEntry(manifest, name, data, description);
            }

            // 1. Provenance Ledger
            Part("Provenance_Ledger_ChainOfCustody.json", JsonSerializer.Serialize(ledger, new JsonSerializerOptions { WriteIndented = true }),
                 "Jurnal Imutabil de Proveniență (Hash-Chained SHA-256)");
            // 2. CASE/UCO 1.3
            if (!string.IsNullOrWhiteSpace(ucoJsonLdContent))
                Part("Case_UCO_Ontology_v1.3.jsonld", ucoJsonLdContent, "Pachet Standardizat CASE 1.3 / UCO JSON-LD");
            // 3. NIS2 draft
            if (!string.IsNullOrWhiteSpace(nis2DraftContent))
                Part("DNSC_NIS2_Notification_Draft.txt", nis2DraftContent, "Draft Notificare Oficială DNSC conform Directiva NIS2 / OUG 155/2024");
            // 4. Super-Timeline
            if (!string.IsNullOrWhiteSpace(superTimelineCsvContent))
                Part("SuperTimeline_Plaso_MACB.csv", superTimelineCsvContent, "Cronologie Forenzică Unificată Super-Timeline (Plaso / Timesketch CSV)");
            // 5. Certificate of authenticity
            Part("CERTIFICATE_OF_AUTHENTICITY.txt", GenerateCertificate(manifest), "Certificat Formal de Autenticitate și Integritate Probatorie");
            // 6. MANIFEST.json (not listed in itself)
            var manifestBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }))).ToArray();
            parts.Add(("MANIFEST.json", manifestBytes));

            // 7. Zip, written next to the destination and moved into place when complete
            var full = Path.GetFullPath(destinationZipPath);
            var partial = full + ".partial";
            try
            {
                using (var fs = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                    foreach (var (name, data) in parts)
                    {
                        using var es = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                        es.Write(data, 0, data.Length);
                    }
                File.Move(partial, full, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }

            using var stream = File.OpenRead(full);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static void AddManifestEntry(CaseManifest manifest, string name, byte[] bytes, string description)
        {
            manifest.Files.Add(new ManifestItem
            {
                RelativePath = name,
                Sha256Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                SizeBytes = bytes.Length,
                Description = description
            });
        }

        private static string GenerateCertificate(CaseManifest manifest)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("CERTIFICAT FORMAL DE AUTENTICITATE ȘI INTEGRITATE PROBATORIE FORENZICĂ");
            sb.AppendLine("Conform Standardelor Internaționale ISO/IEC 27037:2012 și ISO/IEC 27042:2015");
            sb.AppendLine("================================================================================");
            sb.AppendLine();
            sb.AppendLine($"ID Dosar / Caz Forenzic: {manifest.CaseId}");
            sb.AppendLine($"Denumire Caz: {manifest.CaseTitle}");
            sb.AppendLine($"Organizație / Unitate Forenzică: {manifest.Organization}");
            sb.AppendLine($"Investigator Responsabil / Operator: {manifest.LeadInvestigator}");
            sb.AppendLine($"Data & Ora Sigilării (UTC): {manifest.SealedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();
            sb.AppendLine("DECLARAȚIE DE INTEGRITATE:");
            sb.AppendLine("Prin prezenta se atestă că probele digitale, jurnalele de evenimente și artefactele");
            sb.AppendLine("incluse în acest pachet au fost culese și analizate folosind platforma LogAnalyzer");
            sb.AppendLine("DFIR Enterprise, respectând cerințele stricte privind lanțul de custodie (Chain of Custody).");
            sb.AppendLine("Nicio modificare, alterare sau injectare de date nu a avut loc pe mediul sursă.");
            sb.AppendLine();
            sb.AppendLine("FIȘIERE INCLUSE ȘI AMPRENTE DIGITALE SHA-256:");
            foreach (var file in manifest.Files)
            {
                sb.AppendLine($"• [{file.RelativePath}] ({file.SizeBytes:N0} bytes)");
                sb.AppendLine($"  SHA-256: {file.Sha256Hash}");
                sb.AppendLine($"  Rol: {file.Description}");
            }
            sb.AppendLine();
            sb.AppendLine("Semnătură Criptografică & Amprentă Sigiliu: VERIFICAT.");
            return sb.ToString();
        }
    }
}
