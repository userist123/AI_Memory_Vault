using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Policy;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>
/// Policy lifecycle in the sealed authentication audit (independent review, finding 2; review of the remediations, findings 1-3). The auth folder
/// is writable only by administrators and its audit head is sealed with a machine key, so a registration, approval, application, rollback or
/// retirement cannot be forged by editing the per-user policy store, execution records or their .sha256 sidecars.
/// </summary>
public sealed partial class AuthService : IPolicyApprovalLedger
{
    public const string PolicyApprovedEvent = "policy.approved";
    public const string PolicyRegisteredEvent = "policy.registered";
    public const string PolicyRetiredEvent = "policy.retired";
    public const string PolicyAppliedEvent = "policy.applied";
    public const string PolicyRolledBackEvent = "policy.rolled_back";

    internal static string ApprovalKey(string policyId, string version, string sha256) => $"{policyId}@{version} sha256:{sha256.ToUpperInvariant()}";

    private static string RegistrationDetail(string policyId, string version, string sha256, string author) =>
        $"{ApprovalKey(policyId, version, sha256)} author:{author.Trim()}";

    private static string AppliedDetail(string policyId, string version, string sha256, string executionId, string recordSha256, string valuesSha256) =>
        $"{ApprovalKey(policyId, version, sha256)} exec:{executionId} record:{recordSha256.ToUpperInvariant()} values:{valuesSha256.ToUpperInvariant()}";

    public void RecordApproval(string policyId, string version, string policySha256, string approver)
    {
        lock (_gate) Audit(PolicyApprovedEvent, approver, approver, "ok", "policy_approved", ApprovalKey(policyId, version, policySha256));
    }

    public void RecordRegistration(string policyId, string version, string policySha256, string author, string account)
    {
        lock (_gate) Audit(PolicyRegisteredEvent, account, account, "ok", "policy_registered", RegistrationDetail(policyId, version, policySha256, author));
    }

    public void RecordRetirement(string policyId, string version, string policySha256, string account)
    {
        lock (_gate) Audit(PolicyRetiredEvent, account, account, "ok", "policy_retired", ApprovalKey(policyId, version, policySha256));
    }

    public void RecordApplied(string policyId, string version, string policySha256, string executionId, string recordSha256, string valuesSha256, string account)
    {
        lock (_gate) Audit(PolicyAppliedEvent, account, account, "ok", "policy_applied", AppliedDetail(policyId, version, policySha256, executionId, recordSha256, valuesSha256));
    }

    public void RecordRolledBack(string policyId, string version, string policySha256, string rollbackId, string sourceExecutionId, string recordSha256, string account)
    {
        lock (_gate) Audit(PolicyRolledBackEvent, account, account, "ok", "policy_rolled_back",
            $"{ApprovalKey(policyId, version, policySha256)} exec:{rollbackId} of:{sourceExecutionId} record:{recordSha256.ToUpperInvariant()}");
    }

    public IReadOnlyList<PolicyApprovalEntry> Approvals(string policyId, string version, string policySha256)
    {
        lock (_gate)
        {
            var key = ApprovalKey(policyId, version, policySha256);
            var list = new List<PolicyApprovalEntry>();
            // In audit order: a retirement of this text cancels every approval written before it (a new approval is needed after it).
            foreach (var e in TrustedEntries())
            {
                if (e.Result != "ok" || e.Detail != key) continue;
                if (e.Event == PolicyRetiredEvent) list.Clear();
                else if (e.Event == PolicyApprovedEvent) list.Add(new PolicyApprovalEntry(e.Account, e.WhenUtc));
            }
            return list;
        }
    }

    public IReadOnlyList<string> RegisteringAccounts(string policyId, string version, string policySha256, string author)
    {
        lock (_gate)
        {
            var detail = RegistrationDetail(policyId, version, policySha256, author);
            return TrustedEntries().Where(e => e.Event == PolicyRegisteredEvent && e.Result == "ok" && e.Detail == detail).Select(e => e.Account).ToList();
        }
    }

    public bool WasApplied(string policyId, string version, string policySha256, string executionId, string recordSha256, string valuesSha256, string account)
    {
        lock (_gate)
        {
            var detail = AppliedDetail(policyId, version, policySha256, executionId, recordSha256, valuesSha256);
            return TrustedEntries().Any(e => e.Event == PolicyAppliedEvent && e.Result == "ok" && e.Detail == detail && SameName(e.Account, account));
        }
    }

    /// <summary>The audit lines, after the keyed chain verified; a broken chain cannot vouch for any policy step.</summary>
    private IReadOnlyList<AuthAuditEntry> TrustedEntries()
    {
        if (!File.Exists(AuditPath)) return [];
        if (VerifyAudit().Status == ChainStatus.Broken)
            throw new PolicyLifecycleException("Jurnalul de autentificare sigilat este deteriorat sau modificat: pașii politicii nu pot fi verificați. Nu s-a schimbat nimic.");
        return Json.ReadLines<AuthAuditEntry>(AuditPath).ToList();
    }
}
