using System.Globalization;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Case;

/// <summary>
/// The application install date recorded with the anchor whose code was typed from the paper (check A16). <see cref="Determinable"/> is false without a typed
/// code; <see cref="Recorded"/> is false when that anchor carries no date (recorded before the check existed); <see cref="Utc"/> null = the date was unknown then.
/// </summary>
public sealed record AnchoredInstall(bool Determinable, bool Recorded, DateTimeOffset? Utc, string Basis, string Why);

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
        return new(true, true, utc, b.Success ? b.Groups[2].Value : "", "");
    }
}
