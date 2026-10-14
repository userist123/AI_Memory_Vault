using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Policy;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>
/// Policy approvals in the sealed authentication audit (independent review, finding 2). The auth folder is writable only by administrators
/// and its audit head is sealed with a machine key, so an approval cannot be forged by editing the per-user policy store.
/// </summary>
public sealed partial class AuthService : IPolicyApprovalLedger
{
    public const string PolicyApprovedEvent = "policy.approved";

    internal static string ApprovalKey(string policyId, string version, string sha256) => $"{policyId}@{version} sha256:{sha256.ToUpperInvariant()}";

    public void RecordApproval(string policyId, string version, string policySha256, string approver)
    {
        lock (_gate) Audit(PolicyApprovedEvent, approver, approver, "ok", "policy_approved", ApprovalKey(policyId, version, policySha256));
    }

    public IReadOnlyList<PolicyApprovalEntry> Approvals(string policyId, string version, string policySha256)
    {
        lock (_gate)
        {
            if (!File.Exists(AuditPath)) return [];
            if (VerifyAudit().Status == ChainStatus.Broken)
                throw new PolicyLifecycleException("Jurnalul de autentificare sigilat este deteriorat sau modificat: aprobarea politicii nu poate fi verificată. Nu s-a schimbat nimic.");
            var key = ApprovalKey(policyId, version, policySha256);
            return Json.ReadLines<AuthAuditEntry>(AuditPath)
                .Where(e => e.Event == PolicyApprovedEvent && e.Result == "ok" && e.Detail == key)
                .Select(e => new PolicyApprovalEntry(e.Account, e.WhenUtc)).ToList();
        }
    }
}
