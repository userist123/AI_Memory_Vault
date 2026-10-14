using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Policy;

/// <summary>One approval of one policy text (id, version, SHA-256) as written in the sealed authentication audit.</summary>
public sealed record PolicyApprovalEntry(string Approver, DateTimeOffset AtUtc);

/// <summary>
/// Where policy approvals are recorded with authority (independent review, finding 2): the keyed, hash-chained authentication audit in the
/// administrators-only auth folder, not the per-user <c>store.json</c>, which any process of that user can rewrite. The JSON store remains the
/// lifecycle cache; the approval that lets a policy change the station is the one this ledger confirms.
/// </summary>
public interface IPolicyApprovalLedger
{
    /// <summary>Appends the approval; throws when it cannot be written (the approval is then not made).</summary>
    void RecordApproval(string policyId, string version, string policySha256, string approver);

    /// <summary>The approvals recorded for this exact text; throws <see cref="PolicyLifecycleException"/> when the audit cannot be trusted (broken chain).</summary>
    IReadOnlyList<PolicyApprovalEntry> Approvals(string policyId, string version, string policySha256);
}

/// <summary>
/// The approval ledger the application registers at startup, and the acting account of a policy step. With sign-in required, the account is the
/// signed-in one (a typed name that differs is refused) and the ledger is mandatory; without sign-in (tests, headless tools) the caller-supplied
/// name and the JSON store are used as before.
/// </summary>
public static class PolicyApprovals
{
    private sealed record Override(IPolicyApprovalLedger? Ledger);
    private static volatile IPolicyApprovalLedger? _global;
    private static readonly AsyncLocal<Override?> Scoped = new();

    public static IPolicyApprovalLedger? Ledger { get => Scoped.Value is { } o ? o.Ledger : _global; set => _global = value; }

    /// <summary>Overrides the ledger for the current async flow only (tests). Dispose restores the previous one.</summary>
    public static IDisposable Scope(IPolicyApprovalLedger? ledger)
    {
        var previous = Scoped.Value;
        Scoped.Value = new Override(ledger);
        return new Restore(previous);
    }

    private sealed class Restore(Override? previous) : IDisposable { public void Dispose() => Scoped.Value = previous; }

    /// <summary>The account that performs the step: the signed-in account when sign-in is required, otherwise the supplied name.</summary>
    internal static string ActingAccount(string supplied)
    {
        if (!OperatorIdentity.AuthenticationRequired)
        {
            if (string.IsNullOrWhiteSpace(supplied)) throw new PolicyLifecycleException("Pasul cere numele operatorului.");
            return supplied.Trim();
        }
        var signedIn = OperatorIdentity.SignedInAccount ?? throw new HostChangeNotAuthorizedException(OperatorIdentity.HostChangeRefusedMessage);
        if (!string.IsNullOrWhiteSpace(supplied) && !AuthService.SameName(supplied, signedIn))
            throw new PolicyLifecycleException($"Pasul se face în numele contului autentificat {signedIn}, nu al „{supplied.Trim()}”. Nu s-a schimbat nimic.");
        return signedIn;
    }

    /// <summary>The ledger to use; null only when sign-in is not required and none is registered (tests, tools).</summary>
    internal static IPolicyApprovalLedger? RequiredLedger()
    {
        var l = Ledger;
        if (l is null && OperatorIdentity.AuthenticationRequired)
            throw new PolicyLifecycleException("Jurnalul de autentificare sigilat nu este disponibil, deci aprobarea politicii nu poate fi înregistrată sau verificată. Nu s-a schimbat nimic.");
        return l;
    }
}
