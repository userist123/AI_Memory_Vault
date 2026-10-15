using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.Dfir.Case;

/// <summary>
/// F2: what every PDF report (investigation, control, transfer register) re-verifies immediately before it is generated, and the one line it prints on
/// page 1: counts of evidence whose SHA-256 still matches acquisition (OK), changed, missing, and those that could not be verified. A changed or missing
/// item, or a broken custody / audit chain, blocks the word "confirmat" in the report (<see cref="Unconfirm"/>): a result resting on evidence that is no
/// longer the acquired one cannot be called confirmed.
/// </summary>
public sealed record PreReportCheck(int Ok, int Changed, int Missing, int Unverified, bool Applicable, bool ChainBroken, DateTimeOffset AtUtc, string Scope)
{
    public int Total => Ok + Changed + Missing + Unverified;

    /// <summary>True when the report must not use "confirmat" wording.</summary>
    public bool BlocksConfirmed => Changed > 0 || Missing > 0 || ChainBroken;

    /// <summary>The result line printed on page 1 (Romanian).</summary>
    public string Line
    {
        get
        {
            if (!Applicable)
                return $"Reverificarea probelor înainte de raport ({AtUtc:yyyy-MM-dd HH:mm} UTC): nedeterminat — {Scope}.";
            var line = $"Reverificarea probelor înainte de raport ({AtUtc:yyyy-MM-dd HH:mm} UTC, {Scope}): {Ok} OK · {Changed} modificate · {Missing} lipsă" +
                       (Unverified > 0 ? $" · {Unverified} neverificabile" : "") + (ChainBroken ? " · lanț de custodie sau audit RUPT" : "") + ".";
            if (BlocksConfirmed)
                line += " Formularea „confirmat” este blocată în acest raport: cel puțin o probă nu mai este cea achiziționată.";
            return line;
        }
    }

    /// <summary>Re-checks every evidence item of a case through <see cref="CaseWorkspace.Recheck"/> (re-hash, chains, outputs; audited as <c>case.recheck</c>).</summary>
    public static PreReportCheck ForCase(CaseWorkspace? ws, CancellationToken ct = default)
    {
        if (ws is null) return new(0, 0, 0, 0, false, false, DateTimeOffset.UtcNow, "raport generat fără caz, nu există probe de reverificat");
        var r = ws.Recheck(ct);
        bool broken = r.Chains.Custody.Status == ChainStatus.Broken || r.Chains.Audit.Status == ChainStatus.Broken;
        var check = new PreReportCheck(
            r.Evidence.Count(e => e.Status == EvidenceCheckStatus.Intact),
            r.Evidence.Count(e => e.Status == EvidenceCheckStatus.Modified),
            r.Evidence.Count(e => e.Status == EvidenceCheckStatus.Missing),
            r.Evidence.Count(e => e.Status is EvidenceCheckStatus.NoBaseline or EvidenceCheckStatus.Unreadable),
            true, broken, r.AtUtc, $"caz {ws.Info.CaseId}");
        ws.Audit("report.precheck", check.Line);
        return check;
    }

    /// <summary>
    /// Transfer register: re-runs the content verification of the last check (declared SHA-256 against the medium) when the medium is still available.
    /// Without a previous verification or with the medium gone the line says "nedeterminat"; it never reuses the old result as if it were fresh.
    /// </summary>
    public static PreReportCheck ForTransfer(TransferVerification? last, IReadOnlyList<TransferRow> allTransfers, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (last is null) return new(0, 0, 0, 0, false, false, now, "nu a fost rulată „Verifică transferul după conținut”, deci nu există sume SHA-256 de reverificat pe suport");
        if (last.Root.Length == 0 || !Directory.Exists(last.Root))
            return new(0, 0, 0, 0, false, false, now, $"suportul verificat la {last.StartedUtc:yyyy-MM-dd HH:mm} UTC nu mai este disponibil; conectați-l și refaceți raportul");
        var scope = allTransfers.Where(t => last.TransferNumbers.Contains(t.RegistrationNumber, StringComparer.OrdinalIgnoreCase)).ToList();
        var v = TransferVerifier.Verify(scope, allTransfers, last.Root, new TransferVerifyOptions { ReportUnlisted = false }, null, ct);
        return new(v.Count(DeclaredFileStatus.Identical), v.Count(DeclaredFileStatus.DifferentContent), v.Count(DeclaredFileStatus.NotFound),
                   v.Count(DeclaredFileStatus.SameName) + v.Count(DeclaredFileStatus.Unreadable), true, false, now, $"{v.Declared.Count} fișiere declarate, pe suport");
    }

    private static readonly Regex Confirmed = new(@"(?<![\p{L}])(?<!de\s)(?<c>[Cc])onfirmat(?<s>ă|e|i|ul)?(?![\p{L}])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>Rewrites "confirmat/confirmată/confirmate" to the negative form when <see cref="BlocksConfirmed"/>; "de confirmat" (to be confirmed) and "neconfirmat" stay.</summary>
    public string Unconfirm(string text) => BlocksConfirmed ? Neutralize(text) : text;

    public static string Neutralize(string text) =>
        Confirmed.Replace(text, m => (m.Groups["c"].Value == "C" ? "Neconfirmat" : "neconfirmat") + m.Groups["s"].Value);

    /// <summary>True when the text still contains a positive "confirmat" word (tests and the report guard).</summary>
    public static bool HasConfirmedWording(string text) => Confirmed.IsMatch(text);
}
