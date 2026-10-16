using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Media history across stations without UI (owner decision 2026-10-16). On each station: the export is signed with the security officer's qualified card
    /// certificate when the officer collects it (detached CMS with SignedCms, the key never leaves the card), recorded in the sealed audit and written for
    /// transport on the officer's own medium. On the officer's station: the exports are verified (pinned officer, chain, CRL), reconciled, and the report is
    /// generated with the reserved signature field and signed with the card. No network: the transport is the officer's removable medium.
    /// </summary>
    public static class MediaHistoryFlows
    {
        public sealed record Signed(MediaHistoryDoc Doc, byte[] Json, byte[] Cms, string Sha256, string SignerFingerprint);

        /// <summary>
        /// The export of this station from the control just run (its classified exits, file hashes, transfer register, signed register in force and anchor) and the
        /// device inventory read now. File auditing is what A14 found: CONFORM = "da", NECONFORM = "nu", anything else = "necunoscut".
        /// </summary>
        public static MediaHistoryDoc FromControl(LogAnalyzer.Dfir.Windows.Audit.ControlReport r, IReadOnlyList<LogAnalyzer.Dfir.Devices.DeviceRecord> devices, string stationId,
                                                  DateTimeOffset nowUtc, IReadOnlyList<string>? notes = null)
        {
            var f = r.Facts;
            var a14 = r.Checks.FirstOrDefault(c => c.Id == "A14")?.Status;
            var auditing = a14 == LogAnalyzer.Dfir.Windows.Audit.ControlStatus.Conform ? "da" : a14 == LogAnalyzer.Dfir.Windows.Audit.ControlStatus.Neconform ? "nu" : "necunoscut";
            var extra = new List<string>(notes ?? []);
            if (!f.IsAdministrator) extra.Add("controlul a rulat fără drepturi de administrator: jurnalul Security nu a fost citit, activitatea pe fișiere lipsește");
            if (r.Classified is null) extra.Add("profilul de proceduri nu definește foldere clasificate: activitatea pe fișiere și amprentele lor nu sunt în export");
            return MediaHistoryExport.Build(new MediaHistoryInput
            {
                StationId = stationId, StationName = f.Host, Edition = LogAnalyzer.Core.Services.Edition.StationPostures.EditionLabel(f.Posture), Posture = f.Posture.ToString(),
                TimeZone = TimeZoneInfo.Local.Id, NowUtc = nowUtc, Anchor = r.Anchor, Register = f.SignedMedia, Devices = devices, Classified = r.Classified,
                FileHashes = f.ClassifiedHashes, Transfers = f.Transfers, FileAuditing = auditing, Notes = extra,
                // review R5: the tamper indicators of this control travel with the export (A05, A11, A12, A13, A16, S05); a check that did not run is "lipsă"
                Indicators = [.. r.Checks.Where(c => MediaHistoryExport.IndicatorIds.Contains(c.Id)).Select(c => new StationIndicator
                {
                    Id = c.Id, Title = c.Title, Status = LogAnalyzer.Dfir.Windows.Audit.ControlReportPdf.StatusText(c.Status),
                    Detail = c.Detail.Length > 600 ? c.Detail[..600] + "…" : c.Detail,
                })],
            });
        }

        /// <summary>Signs the canonical bytes of <paramref name="doc"/>; refuses a certificate that is not a trusted registered officer or an invalid export.</summary>
        public static Signed Sign(MediaHistoryDoc doc, X509Certificate2 cert, IReadOnlyList<SecurityOfficerRow> trusted)
        {
            var problems = MediaHistoryExport.Problems(doc);
            if (problems.Count > 0) throw new InvalidOperationException("Exportul nu poate fi semnat: " + string.Join("; ", problems) + ".");
            var fp = OfficerFingerprint.Of(cert);
            if (!trusted.Any(t => OfficerFingerprint.Same(t.Fingerprint, fp)))
                throw new InvalidOperationException($"Certificatul „{OfficerFingerprint.CommonName(cert)}” (amprenta {fp}) nu aparține unui ofițer de securitate înregistrat pe această stație.");
            var json = MediaHistoryExport.Canonical(doc);
            return new Signed(doc, json, new SignedCmsSigner(cert).Sign(json), SignedMediaRegister.Sha256Hex(json), fp);
        }

        /// <summary>
        /// Records "media_history.exported" in the sealed audit FIRST and writes the export and its ".p7s" only when the entry was written (no silent export);
        /// returns the JSON path.
        /// </summary>
        public static string Export(string folder, Signed s, Func<string, string, bool> audit)
        {
            var name = MediaHistoryExport.FileName(s.Doc);
            if (!audit(OperationAudit.MediaHistoryExported, OperationAudit.MediaHistoryExportedDetail(name, s.Sha256, s.SignerFingerprint, s.Doc.Media.Count, s.Doc.Transfers.Count, s.Doc.AnchorSeq)))
                throw new InvalidOperationException("exportul nu s-a scris: intrarea în jurnalul aplicației nu a putut fi scrisă (exportul se face doar cu urmă în jurnal)");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, s.Json);
            File.WriteAllBytes(path + MediaHistoryExport.SignatureExtension, s.Cms);
            return path;
        }

        /// <summary>Verifies every export file (its ".p7s" next to it; a missing one is "nesemnat").</summary>
        public static List<MediaHistoryCheck> Load(IEnumerable<string> jsonPaths, IReadOnlyList<SecurityOfficerRow> pinned, QualifiedSignatureTrust trust, ICmsDetachedVerifier? verifier = null)
        {
            var list = new List<MediaHistoryCheck>();
            foreach (var p in jsonPaths)
            {
                var name = Path.GetFileName(p);
                byte[] json;
                try { json = File.ReadAllBytes(p); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    list.Add(new MediaHistoryCheck(false, name, "fișierul nu poate fi citit: " + ex.Message, [new VerificationLine(false, ex.Message)], null, "", "", ""));
                    continue;
                }
                var sig = p + MediaHistoryExport.SignatureExtension;
                byte[]? cms = File.Exists(sig) ? File.ReadAllBytes(sig) : null;
                list.Add(MediaHistoryVerify.Verify(name, json, cms, verifier ?? SignedCmsVerifier.Instance, pinned, trust));
            }
            return list;
        }

        public sealed record ReportFiles(string SignedPdf, string Csv, string UnsignedSha256, string SignedSha256);

        /// <summary>
        /// Generates the reconciliation report (reserved signature field), records its hash as "report.generated", has the officer sign it with the card, writes the
        /// signed PDF and the CSV table to <paramref name="folder"/> and records "media_history.reconciled". Nothing is written when the audit refuses the entry.
        /// </summary>
        public static ReportFiles ExportReport(string folder, CrossStationResult r, CrossStationSettings s, string officerStation, string registerLine, X509Certificate2 cert,
                                               IReadOnlyList<SecurityOfficerRow> trusted, DateTimeOffset nowUtc, Func<string, string, bool> audit)
        {
            var fp = OfficerFingerprint.Of(cert);
            if (!trusted.Any(t => OfficerFingerprint.Same(t.Fingerprint, fp)))
                throw new InvalidOperationException($"Certificatul „{OfficerFingerprint.CommonName(cert)}” (amprenta {fp}) nu aparține unui ofițer de securitate înregistrat.");
            var pdf = LogAnalyzer.Dfir.Windows.Audit.CrossStationReportPdf.Generate(r, s, officerStation, registerLine, nowUtc);
            var stamp = nowUtc.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var unsignedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdf));
            var baseName = $"confruntare_statii_{stamp}";
            if (!audit(OperationAudit.ReportGenerated, OperationAudit.ReportGeneratedDetail(baseName + ".pdf", unsignedSha)))
                throw new InvalidOperationException("raportul nu s-a generat: intrarea în jurnalul aplicației nu a putut fi scrisă");
            var signed = OfficerFlows.Sign(pdf, cert, officerStation, "Confruntare intre statii semnata de ofiterul de securitate", nowUtc);
            var signedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(signed));
            if (!audit(OperationAudit.MediaHistoryReconciled, OperationAudit.MediaHistoryReconciledDetail(baseName + "_semnat_ofiter.pdf", signedSha, r.Accepted.Count, r.Rejected.Count,
                                                                                                            r.Count(CrossSeverity.Grav), r.Count(CrossSeverity.Ridicat))))
                throw new InvalidOperationException("raportul semnat nu s-a scris: intrarea în jurnalul aplicației nu a putut fi scrisă");
            Directory.CreateDirectory(folder);
            var pdfPath = Path.Combine(folder, baseName + "_semnat_ofiter.pdf");
            var csvPath = Path.Combine(folder, baseName + ".csv");
            File.WriteAllBytes(pdfPath, signed);
            File.WriteAllText(csvPath, LogAnalyzer.Dfir.Windows.Audit.CrossStationReportPdf.Csv(r), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return new ReportFiles(pdfPath, csvPath, unsignedSha, signedSha);
        }
    }
}
