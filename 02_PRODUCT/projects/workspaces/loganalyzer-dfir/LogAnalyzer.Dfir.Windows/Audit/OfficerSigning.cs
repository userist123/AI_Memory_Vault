using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>Outcome of signing the official copy: the PDF, its detached officer signature and who signed.</summary>
public sealed record OfficialReportResult(string Pdf, string SignatureFile, string Officer, string Fingerprint, string Sha256, string AnchorCode);

/// <summary>
/// "Semnează ca ofițer de securitate" and "Înregistrează ofițer de securitate", without UI (the view model asks for files and passphrases and calls these).
/// The officer key is opened only inside these calls and disposed before they return.
/// </summary>
public static class OfficerSigning
{
    /// <summary>
    /// Opens the officer key, refuses a key that is not a trusted registered officer, renders the official copy with the officer's section filled in
    /// (<paramref name="renderOfficial"/> writes it and returns its path), then signs exactly those bytes. The report object is left unsigned afterwards.
    /// </summary>
    public static OfficialReportResult SignOfficial(ControlReport report, ProcedureProfile? profile, string officerKeyFileText, ReadOnlySpan<char> passphrase,
                                                    Func<ControlReport, string> renderOfficial, DateTimeOffset nowUtc)
    {
        using var key = OfficerKey.Load(officerKeyFileText, passphrase);
        var fp = OfficerKey.Fingerprint(OfficerKey.PublicKeyOf(key));
        var officer = OfficerRegistry.Trusted(profile, out _).FirstOrDefault(r => OfficerKey.Same(r.Fingerprint, fp))
            ?? throw new InvalidOperationException($"Cheia cu amprenta {fp} nu aparține unui ofițer de securitate înregistrat în profilul de proceduri. Raportul nu a fost semnat.");
        report.Officer = new OfficerSignatureBlock(officer.Name, fp, nowUtc.ToUniversalTime());
        try
        {
            var pdf = renderOfficial(report);
            var sig = OfficerSignature.Sign(pdf, key, officer.Name, report.Anchor?.Code ?? "", nowUtc);
            return new OfficialReportResult(pdf, OfficerSignature.SigPath(pdf), officer.Name, fp, sig.Sha256, sig.AnchorCode);
        }
        finally { report.Officer = null; }
    }

    /// <summary>
    /// Adds an officer to <paramref name="profile"/> (not saved here). The new officer proves possession of the key by typing its passphrase. With no officer
    /// yet, this is the first officer (its fingerprint must go on the proces-verbal); otherwise an already registered officer must approve with their key.
    /// </summary>
    public static SecurityOfficerRow Register(ProcedureProfile profile, string name, string newKeyFileText, ReadOnlySpan<char> newPassphrase,
                                              string? approverKeyFileText, ReadOnlySpan<char> approverPassphrase, DateTimeOffset nowUtc)
    {
        string pub;
        using (var k = OfficerKey.Load(newKeyFileText, newPassphrase)) pub = OfficerKey.PublicKeyOf(k);
        profile.SecurityOfficers ??= [];
        if (profile.SecurityOfficers.Count == 0) return OfficerRegistry.RegisterFirst(profile, name, pub, nowUtc);
        if (approverKeyFileText is null)
            throw new InvalidOperationException("Există deja ofițeri de securitate înregistrați: înregistrarea unui ofițer nou trebuie aprobată cu cheia și parola unui ofițer existent.");
        using var approver = OfficerKey.Load(approverKeyFileText, approverPassphrase);
        return OfficerRegistry.RegisterApproved(profile, name, pub, approver, nowUtc);
    }
}
