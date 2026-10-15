namespace LogAnalyzer.Dfir.Reporting;

public enum OfficialVerdict { Oficial, ValidNeoficial, Invalid }

/// <summary>One line of the verdict, explained: passed (true), failed (false) or could not be confirmed (null).</summary>
public sealed record VerificationLine(bool? Passed, string Text)
{
    public string Mark => Passed switch { true => "DA", false => "NU", _ => "NECONFIRMAT" };
}

public sealed record OfficialVerification(OfficialVerdict Verdict, string Headline, IReadOnlyList<VerificationLine> Lines, string SignerFingerprint, string SignerName,
                                          string OriginalSha256, string SignedSha256, DateTimeOffset? ClaimedSigningTime)
{
    public string VerdictWord => Verdict switch { OfficialVerdict.Oficial => "OFICIAL", OfficialVerdict.ValidNeoficial => "VALID, DAR NEOFICIAL", _ => "INVALID" };

    public string ToPlainText() => VerdictWord + " — " + Headline + Environment.NewLine + string.Join(Environment.NewLine, Lines.Select(l => $"[{l.Mark}] {l.Text}"));
}

/// <summary>
/// The verdict on a signed control report (signed in the application or in Adobe with the officer's qualified certificate):
/// OFICIAL only when every check passes AND the signer's certificate fingerprint was confirmed from the signed paper (proces-verbal);
/// VALID, DAR NEOFICIAL when the signature is sound but rests only on the station's own officer list, or the chain / revocation cannot be confirmed offline;
/// INVALID with the precise reason otherwise.
/// </summary>
public static class OfficialReportVerifier
{
    public static OfficialVerification Verify(byte[] signedPdf, ICmsDetachedVerifier cms, IReadOnlyCollection<string> generatedReportSha256s,
                                              IReadOnlyList<SecurityOfficerRow> trustedOfficers, string? paperFingerprint, QualifiedSignatureTrust trust)
    {
        var lines = new List<VerificationLine>();
        var pdf = PdfSignatureCheck.Check(signedPdf, cms);
        string fp = pdf.Signer is { } sc ? OfficerFingerprint.Of(sc) : "", name = pdf.Signer is { } sn ? OfficerFingerprint.CommonName(sn) : "";
        OfficialVerification Done(OfficialVerdict v, string headline, string orig = "") =>
            new(v, headline, lines, fp, name, orig, pdf.SignedFileSha256, pdf.ClaimedSigningTime);

        if (pdf.Problem is { } why)
        {
            lines.Add(new(false, "Semnătura din document: " + why + "."));
            return Done(OfficialVerdict.Invalid, pdf.NotSigned ? "raportul nu este semnat" : why);
        }
        lines.Add(new(true, $"Semnătura din document acoperă tot fișierul în afară de locul semnăturii, nimic nu a fost adăugat după ea, iar semnătura criptografică ({pdf.SubFilter}) corespunde conținutului."));

        var orig = pdf.MatchOriginal(generatedReportSha256s);
        if (orig is null)
        {
            lines.Add(new(false, generatedReportSha256s.Count == 0
                ? "Raportul original: nu există pe această stație amprenta unui raport generat cu care să se compare."
                : "Raportul original: partea semnată nu este identică cu niciun raport generat de aplicație (amprentele din jurnal) — raportul a fost modificat înainte de semnare."));
            return Done(OfficialVerdict.Invalid, "raportul a fost modificat înainte de semnare sau nu este un raport generat de aplicație");
        }
        lines.Add(new(true, $"Raportul original este exact cel generat de aplicație (SHA-256 {orig.Value.Sha256}); semnătura a fost adăugată la sfârșit, fără să-l schimbe."));

        var cert = pdf.Signer!;
        var at = pdf.ClaimedSigningTime ?? DateTimeOffset.UtcNow;
        if (pdf.ClaimedSigningTime is null) lines.Add(new(null, "Documentul nu declară ora semnării; valabilitatea certificatului s-a verificat la ora de acum."));
        var v = trust.Validate(cert, at);
        if (v.Status == QualifiedCheck.Failed)
        {
            lines.Add(new(false, "Certificatul semnatarului: " + v.Message + "."));
            return Done(OfficialVerdict.Invalid, "certificatul semnatarului nu este acceptat: " + v.Message, orig.Value.Sha256);
        }
        lines.Add(new(true, $"Certificatul semnatarului „{name}” este de semnătură calificată (nerepudiere) și era valabil la {at:yyyy-MM-dd HH:mm} UTC."));
        lines.Add(new(v.Status == QualifiedCheck.Ok ? true : null, v.Status == QualifiedCheck.Ok ? "Lanțul certificatului și revocarea: " + v.Message + "."
            : "Lanțul certificatului sau revocarea: " + v.Message + " — importați certificatul autorității și lista de revocare (CRL) a emitentului."));

        bool onStationList = trustedOfficers.Any(t => OfficerFingerprint.Same(t.Fingerprint, fp));
        lines.Add(new(onStationList, onStationList ? $"Semnatarul este ofițer de securitate înregistrat pe această stație (amprenta {fp})."
                                                    : $"Semnatarul (amprenta {fp}) NU este în lista ofițerilor de securitate a acestei stații."));
        bool paperGiven = OfficerFingerprint.Normalize(paperFingerprint).Length > 0;
        bool paperMatches = paperGiven && OfficerFingerprint.Same(paperFingerprint, fp);
        if (paperGiven && !paperMatches)
        {
            lines.Add(new(false, "Amprenta de pe procesul-verbal NU este a certificatului care a semnat."));
            return Done(OfficialVerdict.Invalid, "semnătura nu este a ofițerului de pe procesul-verbal", orig.Value.Sha256);
        }
        if (!onStationList && !paperMatches) return Done(OfficialVerdict.Invalid, "semnatarul nu este ofițer de securitate înregistrat", orig.Value.Sha256);
        lines.Add(new(paperMatches ? true : null, paperMatches ? "Amprenta certificatului este confirmată de pe procesul-verbal semnat."
            : "Amprenta nu a fost comparată cu procesul-verbal: lista stației poate fi schimbată de un administrator. Scrieți amprenta de pe procesul-verbal ca să obțineți verdictul OFICIAL."));
        if (!onStationList) lines.Add(new(null, "Lista ofițerilor de pe stație nu conține acest certificat, deși procesul-verbal îl confirmă: lista a fost schimbată (vezi verificarea A13)."));

        if (paperMatches && v.Status == QualifiedCheck.Ok && onStationList)
            return Done(OfficialVerdict.Oficial, $"semnat de ofițerul de securitate „{name}”, confirmat de pe procesul-verbal", orig.Value.Sha256);
        var reason = !paperMatches ? "semnătură validă față de lista stației — confirmați amprenta de pe procesul-verbal"
                   : v.Status != QualifiedCheck.Ok ? "revocarea sau lanțul certificatului nu pot fi confirmate — importați CRL-ul emitentului"
                   : "lista ofițerilor de pe stație a fost schimbată";
        return Done(OfficialVerdict.ValidNeoficial, reason, orig.Value.Sha256);
    }
}
