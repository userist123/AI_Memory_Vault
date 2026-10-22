using System.Diagnostics;
using LogAnalyzer.Dfir.Response;
using LogAnalyzer.Dfir.Windows.Containment;

namespace LogAnalyzer.Response.Containment;

/// <summary>Result of the audit-policy change: <see cref="Ok"/> only when the re-read of the effective policy confirmed it (VERIFIED).</summary>
public sealed class AuditPolicyChangeResult(ResponseOutcome verification, string detail)
{
    public ResponseOutcome Verification { get; } = verification;
    public string Detail { get; } = detail;
    public bool Ok => Verification.IsSuccess;

    public void Deconstruct(out bool ok, out string detail) { ok = Ok; detail = Detail; }
}

/// <summary>Changes the audit policy of the station (auditpol /set). Call only after the operator has agreed. Unclassified edition only.</summary>
public static class AuditPolicyChange
{
    private const string Action = "auditpol Filtering Platform Connection failure";

    /// <param name="caseAudit">Case audit that receives the outcome and its re-check evidence.</param>
    /// <param name="apply">Seam for tests: runs the change, returns exit code and output. Null = auditpol.exe.</param>
    /// <param name="reread">Seam for tests: re-reads whether failure auditing is on (throws when it cannot be read). Null = AuditQuerySystemPolicy.</param>
    [ControlledResponse("effective audit policy re-read in process (AuditQuerySystemPolicy, failure flag of the subcategory)")]
    public static AuditPolicyChangeResult EnableBlockedConnectionFailureAudit(Action<string, string>? caseAudit = null,
        Func<(int ExitCode, string Output)>? apply = null, Func<bool>? reread = null)
    {
        if (!LogAnalyzer.Dfir.Auth.OperatorIdentity.MayChangeHost)
            return new(ResponseOutcome.NotAttempted(Action, "apply", ResponseStatus.NotAuthorized, LogAnalyzer.Dfir.Auth.OperatorIdentity.HostChangeRefusedMessage),
                LogAnalyzer.Dfir.Auth.OperatorIdentity.HostChangeRefusedMessage);
        (int exit, string output) = (apply ?? RunAuditpol)();
        var probe = new ResponseProbe("Filtering Platform Connection", "auditul eșecurilor activ", () =>
            (reread ?? ReadFailureFlag)() ? ResponseObservation.Holds("auditul eșecurilor este activ") : ResponseObservation.Differs("auditul eșecurilor este inactiv"));
        var v = ResponseVerifier.Verify(Action, "apply", [probe]);
        if (exit != 0 && v.Status == ResponseStatus.Verified)
            v = new ResponseOutcome { Action = Action, Phase = "apply", Status = ResponseStatus.NotVerified, Checks = v.Checks, CheckedUtc = v.CheckedUtc,
                Cause = $"auditpol a întors {exit}, deși re-citirea arată auditul activ: nu se știe dacă schimbarea este a aplicației" };
        else if (exit != 0)
            v = new ResponseOutcome { Action = Action, Phase = "apply", Status = ResponseStatus.Failed, Checks = v.Checks, CheckedUtc = v.CheckedUtc,
                Cause = $"auditpol a întors {exit}; {v.Cause}" };
        ResponseVerifier.Record(v, caseAudit);
        return new(v, $"{v.StatusText}: {v.Cause}" + (output.Length > 0 ? $" ({output})" : "") + (v.AuditError is null ? "" : " " + v.AuditError));
    }

    private static (int, string) RunAuditpol()
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "auditpol.exe"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "/set", $"/subcategory:{BlockedConnectionLog.FilteringPlatformConnectionGuid}", "/failure:enable" }) psi.ArgumentList.Add(a);
        try
        {
            using var p = LogAnalyzer.Dfir.Auth.CollectorExec.Start(psi);
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30_000);
            LogAnalyzer.Dfir.Auth.CollectorExec.Done(p);
            return (p.ExitCode, o.Trim());
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    /// <summary>Failure flag of the subcategory from the effective policy; throws (NOT_VERIFIED) when the policy cannot be read.</summary>
    private static bool ReadFailureFlag()
    {
        var flags = LogAnalyzer.Dfir.Windows.Native.AuditPolicy.QueryFlags(Guid.Parse(BlockedConnectionLog.FilteringPlatformConnectionGuid));
        return (((LogAnalyzer.Dfir.Windows.Native.AuditSetting)(flags & 3)) & LogAnalyzer.Dfir.Windows.Native.AuditSetting.Failure) != 0;
    }
}
