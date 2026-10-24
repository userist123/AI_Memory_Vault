using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>
/// The signed media register found in a case folder (finding B7): the file, whether it may be used, and the reason shown to the operator when it may not.
/// <see cref="Active"/> carries the document only when the register passed the same verification as on the live path.
/// </summary>
public sealed record CaseSignedRegister(string File, ActiveMediaRegister Active, string Note)
{
    public bool Used => Active.Usable;
}

/// <summary>
/// Offline case analysis with the signed media register. When the case folder holds "registru_medii_vNNNN.json" with its ".p7s" the register is verified
/// EXACTLY as an import on the live path (<see cref="SignedMediaRegisterImport.Verify"/>: canonical file, detached CMS over the exact bytes, qualified
/// certificate, signer on the station's officer list, fingerprint confirmed from the proces-verbal): only an OFICIAL register is used. An invalid,
/// unsigned, unconfirmed or altered one is NOT used and the reason is returned; it is never trusted silently and a lower version is never substituted.
/// Only the case root is searched, never the imported evidence subfolders (a planted register there cannot block or replace the case's own). No file in the case: <c>null</c>, behaviour unchanged.
/// </summary>
public static class SignedMediaRegisterCase
{
    private static readonly Regex Name = new(@"^registru_medii_v(?<v>[0-9]{4})\.json\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>A register file larger than this is refused unread (the real ones are a few kilobytes).</summary>
    public const long MaxBytes = 8 * 1024 * 1024;

    public static CaseSignedRegister? Find(string caseRoot, ICmsDetachedVerifier verifier, IReadOnlyList<SecurityOfficerRow> trustedOfficers,
                                           QualifiedSignatureTrust trust, string? paperFingerprint)
    {
        string? best = null; int bestVersion = -1;
        try
        {
            if (!Directory.Exists(caseRoot)) return null;
            foreach (var f in Directory.EnumerateFiles(caseRoot, "registru_medii_v*.json", SearchOption.TopDirectoryOnly))
            {
                var m = Name.Match(Path.GetFileName(f));
                if (!m.Success) continue;
                var v = int.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture);
                if (v > bestVersion || (v == bestVersion && string.CompareOrdinal(f, best) < 0)) { best = f; bestVersion = v; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (best is null) return null;

        var sigPath = best + SignedMediaRegister.SignatureExtension;
        string Refuse(string why) => $"Registrul de medii semnat din caz ({Path.GetFileName(best)}) NU este folosit: {why}.";
        try
        {
            if (new FileInfo(best).Length > MaxBytes) return Not(best, Refuse("fișierul este prea mare"));
            if (!System.IO.File.Exists(sigPath)) return Not(best, Refuse($"lipsește semnătura „{Path.GetFileName(sigPath)}” de lângă el"));
            var json = System.IO.File.ReadAllBytes(best);
            var cms = System.IO.File.ReadAllBytes(sigPath);
            var check = SignedMediaRegisterImport.Verify(json, cms, verifier, trustedOfficers, paperFingerprint, trust, installed: null);
            if (check.Verdict == MediaRegisterVerdict.Oficial && check.Doc is { } doc)
                return new(best, new ActiveMediaRegister(doc, check.Sha256, ""), $"Registrul de medii semnat din caz ({Path.GetFileName(best)}) este OFICIAL și s-a folosit la analiză: {check.Headline} (SHA-256 {check.Sha256}).");
            return new(best, ActiveMediaRegister.None(check.Headline), Refuse(check.Headline));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Not(best, Refuse("nu poate fi citit (" + ex.Message + ")"));
        }

        static CaseSignedRegister Not(string file, string note) => new(file, ActiveMediaRegister.None(note), note);
    }

    /// <summary>The rows of the case-analysis media register built from a verified signed register (active, withdrawn, destroyed with their dates).</summary>
    public static MediaRegister ToMediaRegister(SignedMediaRegisterDoc doc)
    {
        static string Date(string utc) => SignedMediaRegister.TryUtc(utc) is { } t ? t.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        var reg = new MediaRegister { UpdatedUtc = doc.Issued };
        foreach (var e in doc.Entries)
            reg.Rows.Add(new MediaRow
            {
                RegistrationNumber = e.InventoryNumber, Serial = e.UsbSerial, VidPid = e.VidPid, Type = "USB", Classification = e.Classification, AssignedUser = e.Holder,
                ValidFrom = Date(e.EnrolledUtc), ValidTo = e.IsActive ? "" : Date(e.StatusChangedUtc),
                Status = e.Status == SignedMediaRegister.StatusRetired ? "withdrawn" : e.Status == SignedMediaRegister.StatusDestroyed ? "destroyed" : "active",
                Notes = (e.Notes + " [registru semnat v" + doc.Version + "]").Trim(),
            });
        return reg;
    }
}
