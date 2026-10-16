using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// The signed media register without UI (owner decision 2026-10-16): on the transfer station a new version is signed with the security officer's
    /// qualified card certificate (detached CMS with SignedCms, the same signer as the officer's report signature; the key never leaves the card) and exported
    /// for transport; on every station a version is verified and installed (or refused) through <see cref="SignedMediaRegisterImport"/>.
    /// </summary>
    public static class MediaRegisterFlows
    {
        public sealed record Signed(SignedMediaRegisterDoc Doc, byte[] Json, byte[] Cms, string Sha256);

        /// <summary>Signs the canonical bytes of <paramref name="doc"/>; refuses a certificate that is not a trusted registered officer or a register with problems.</summary>
        public static Signed Sign(SignedMediaRegisterDoc doc, X509Certificate2 cert, IReadOnlyList<SecurityOfficerRow> trusted)
        {
            var problems = SignedMediaRegister.Problems(doc);
            if (problems.Count > 0) throw new InvalidOperationException("Registrul nu poate fi semnat: " + string.Join("; ", problems) + ".");
            var fp = OfficerFingerprint.Of(cert);
            if (!trusted.Any(t => OfficerFingerprint.Same(t.Fingerprint, fp)))
                throw new InvalidOperationException($"Certificatul „{OfficerFingerprint.CommonName(cert)}” (amprenta {fp}) nu aparține unui ofițer de securitate înregistrat.");
            var json = SignedMediaRegister.Canonical(doc);
            var cms = new SignedCmsSigner(cert).Sign(json);
            return new Signed(doc, json, cms, SignedMediaRegister.Sha256Hex(json));
        }

        /// <summary>Writes "registru_medii_vNNNN.json" and its ".p7s" into <paramref name="folder"/>; returns the JSON path.</summary>
        public static string Export(string folder, Signed s)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, SignedMediaRegister.FileName(s.Doc.Version));
            File.WriteAllBytes(path, s.Json);
            File.WriteAllBytes(path + SignedMediaRegister.SignatureExtension, s.Cms);
            return path;
        }

        /// <summary>
        /// Verifies the register file and its ".p7s" next to it, writes the outcome to the sealed audit FIRST (<paramref name="audit"/>: media_register.imported
        /// or .refused), then records it in the store; an installable version is installed only when the audit entry was written (audit-first: no silent install).
        /// </summary>
        public static MediaRegisterCheck Import(string jsonPath, string? paperFingerprint, SignedMediaRegisterStore store, IReadOnlyList<SecurityOfficerRow> trusted,
                                                QualifiedSignatureTrust trust, DateTimeOffset nowUtc, Func<string, string, bool> audit)
        {
            var sig = jsonPath + SignedMediaRegister.SignatureExtension;
            if (!File.Exists(sig)) throw new FileNotFoundException($"Lipsește semnătura „{Path.GetFileName(sig)}” lângă registru: un registru fără semnătură nu se importă.", sig);
            var json = File.ReadAllBytes(jsonPath);
            var cms = File.ReadAllBytes(sig);
            var check = SignedMediaRegisterImport.Verify(json, cms, SignedCmsVerifier.Instance, trusted, paperFingerprint, trust, store.Installed());
            bool audited = audit(check.Installable ? LogAnalyzer.Dfir.Auth.OperationAudit.MediaRegisterImported : LogAnalyzer.Dfir.Auth.OperationAudit.MediaRegisterRefused,
                LogAnalyzer.Dfir.Auth.OperationAudit.MediaRegisterDetail(check.Doc?.Version ?? 0, check.Sha256, check.VerdictWord, check.Installable, check.SignerFingerprint, check.Headline));
            if (check.Installable && !audited)
                check = check with { Installable = false, Headline = "nu s-a instalat: intrarea în jurnalul aplicației nu a putut fi scrisă (instalarea se face doar cu urmă în jurnal)" };
            store.Record(check, json, cms, paperFingerprint, nowUtc);
            return check;
        }

        /// <summary>The register in force for the control: the installed version re-verified, compared with the last install recorded in the sealed audit.</summary>
        public static ActiveMediaRegister Active(SignedMediaRegisterStore store, string? auditPath)
        {
            int? last = null;
            DateTimeOffset? first = null;
            if (auditPath is not null)
                try
                {
                    var imported = LogAnalyzer.Dfir.Case.AuditAnchor.Events(auditPath).Where(e => e.Event == LogAnalyzer.Dfir.Auth.OperationAudit.MediaRegisterImported).ToList();
                    last = SignedMediaRegisterStore.LastInstalledVersion(imported.Select(e => e.Detail));
                    first = SignedMediaRegisterStore.FirstInstalledUtc(imported.Select(e => (e.WhenUtc, e.Detail)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return store.LoadActive(SignedCmsVerifier.Instance, last) with { FirstInstalledUtc = first };
        }
    }
}
