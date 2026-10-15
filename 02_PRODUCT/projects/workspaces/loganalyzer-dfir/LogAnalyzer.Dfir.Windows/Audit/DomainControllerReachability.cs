namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>Outcome of the active domain-controller check N13.</summary>
public enum DcProbeOutcome
{
    /// <summary>A domain controller was located at an internal address and LDAP (TCP 389) answered.</summary>
    Reachable,
    /// <summary>Located at an internal address, but LDAP did not answer within the timeout.</summary>
    Unreachable,
    /// <summary>A resolved address is public: the probe opened no TCP connection (the Windows locator traffic had already been sent).</summary>
    PublicAddress,
    /// <summary>No domain controller could be located, or only excluded addresses (loopback, link-local) came back.</summary>
    NotResolved,
    /// <summary>The probe did not run (the network gate refused, or the build has no probe).</summary>
    Skipped,
    /// <summary>The overall deadline (locator, name resolution, connection) passed.</summary>
    TimedOut,
    /// <summary>The control run was cancelled while the probe ran.</summary>
    Cancelled,
}

/// <summary>What the N13 probe found. <see cref="Addresses"/> are the resolved addresses as text; <see cref="Detail"/> is the reason in Romanian.</summary>
public sealed record DomainControllerProbeResult(DcProbeOutcome Outcome, string DcName, IReadOnlyList<string> Addresses, string Detail);

/// <summary>
/// The active N13 probe. Only the Network edition registers an implementation (LogAnalyzer.Connectors); this interface carries no network code,
/// so the Standalone and Internet editions, which have none registered, never probe.
/// </summary>
public interface IDomainControllerProbe
{
    DomainControllerProbeResult Probe(string domain, CancellationToken ct);
}
