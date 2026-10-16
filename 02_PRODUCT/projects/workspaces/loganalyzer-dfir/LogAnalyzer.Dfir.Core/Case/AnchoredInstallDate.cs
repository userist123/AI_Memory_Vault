using System.Globalization;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Case;

/// <summary>
/// The application install date recorded with the anchor whose code was typed from the paper (check A16). <see cref="Determinable"/> is false without a typed
/// code; <see cref="Recorded"/> is false when that anchor carries no date (recorded before the check existed); <see cref="Utc"/> null = the date was unknown then.
/// </summary>
public sealed record AnchoredInstall(bool Determinable, bool Recorded, DateTimeOffset? Utc, string Basis, string Why, ChainIdentity? Chain = null);

/// <summary>The identity of the application's audit chain: its first record (seq 1) — hash, time and kind. A reinitialised chain has another identity.</summary>
public sealed record ChainIdentity(string FirstHash, string FirstWhenUtc, string FirstKind);

public sealed record AnchorWriteResult(AuditAnchorPoint? Anchor, AnchoredInstall? Stored, string? Error);

/// <summary>
/// Writes the anchored state and takes the anchor: (1) "audit.anchor_state" with the install date, the chain identity and the officer state; (2) the anchor of the
/// head, which must be that very entry; (3) "audit.anchor". When the state entry cannot be written, or the head is not that entry, NO anchor is issued (the
/// previous entry is never anchored silently). The values returned in <see cref="AnchorWriteResult.Stored"/> are read back from the log, as written.
/// </summary>
public static class AnchorWriter
{
    public static AnchorWriteResult Write(string auditPath, Func<string, string, bool> record, DateTimeOffset? installUtc, string installBasis, string officerStateDetail, string reason)
    {
        const string NotIssued = "Codul de ancorare nu a fost emis: ";
        var chain = AuditAnchor.ChainIdentityOf(auditPath);
        var detail = LogAnalyzer.Dfir.Auth.OperationAudit.AnchorStateDetail(installUtc, installBasis, chain) + (officerStateDetail.Length > 0 ? "; " + officerStateDetail : "");
        if (!record(LogAnalyzer.Dfir.Auth.OperationAudit.AnchorState, detail))
            return new(null, null, NotIssued + "starea ancorată nu a putut fi scrisă în jurnalul aplicației. Reporniți aplicația ca administrator și repetați controlul.");
        var a = AuditAnchor.Current(auditPath);
        if (a is null || AuditAnchor.Events(auditPath).FirstOrDefault(e => e.Seq == a.Seq) is not { Event: LogAnalyzer.Dfir.Auth.OperationAudit.AnchorState })
            return new(null, null, NotIssued + "ultima intrare a jurnalului nu este starea ancorată tocmai scrisă.");
        var stored = AnchoredInstallDate.At(auditPath, a.Seq);
        record(LogAnalyzer.Dfir.Auth.OperationAudit.AnchorRecorded, LogAnalyzer.Dfir.Auth.OperationAudit.AnchorRecordedDetail(a, reason) + (officerStateDetail.Length > 0 ? "; " + officerStateDetail : ""));
        return new(a, stored, null);
    }
}

/// <summary>
/// Reads the install date of the paper anchor: the "audit.anchor_state" event that IS the anchored entry (seq N of the code typed from the paper), so its content
/// is covered by the code itself; an event written after the anchor is never used.
/// </summary>
public static class AnchoredInstallDate
{
    public const string UtcKey = "install_utc", BasisKey = "install_basis", Unknown = "necunoscuta";

    public static string Format(DateTimeOffset? utc) => utc is { } u ? u.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : Unknown;

    public static AnchoredInstall At(string auditPath, long? paperAnchorSeq)
    {
        if (paperAnchorSeq is not { } n)
            return new(false, false, null, "", "nu s-a introdus codul de ancorare de pe procesul-verbal, deci nu există o dată de referință sigură");
        var anchor = AuditAnchor.Events(auditPath).FirstOrDefault(e => e.Seq == n);
        if (anchor is null || anchor.Event != "audit.anchor_state")
            return new(true, false, null, "", "ancora controlului anterior a fost înregistrată înainte ca data instalării să facă parte din starea ancorată");
        var u = Regex.Match(anchor.Detail, $@"(^|; ){UtcKey}=([^;]*)");
        if (!u.Success) return new(true, false, null, "", "starea ancorată nu conține data instalării");
        var b = Regex.Match(anchor.Detail, $@"(^|; ){BasisKey}=([^;]*)");
        DateTimeOffset? utc = DateTimeOffset.TryParseExact(u.Groups[2].Value, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        string F(string key) => Regex.Match(anchor.Detail, $@"(^|; ){key}=([^;]*)") is { Success: true } m ? m.Groups[2].Value : "";
        var chain = F("chain_first_hash") is { Length: > 0 } h ? new ChainIdentity(h, F("chain_first_utc"), F("chain_first_kind")) : null;
        return new(true, true, utc, b.Success ? b.Groups[2].Value : "", "", chain);
    }
}
