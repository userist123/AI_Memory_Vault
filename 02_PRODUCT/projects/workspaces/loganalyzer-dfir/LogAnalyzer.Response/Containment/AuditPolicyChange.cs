using System.Diagnostics;
using LogAnalyzer.Dfir.Windows.Containment;

namespace LogAnalyzer.Response.Containment;

/// <summary>Changes the audit policy of the station (auditpol /set). Call only after the operator has agreed. Unclassified edition only.</summary>
public static class AuditPolicyChange
{
    public static (bool Ok, string Detail) EnableBlockedConnectionFailureAudit()
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "auditpol.exe"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "/set", $"/subcategory:{BlockedConnectionLog.FilteringPlatformConnectionGuid}", "/failure:enable" }) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30_000);
            return (p.ExitCode == 0, o.Trim());
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
