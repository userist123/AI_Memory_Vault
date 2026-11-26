using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// The session logs of AnyDesk / TeamViewer in the Standard profile (2026-11-15). Before, only "Completă" read them (inside
/// <see cref="UserArtifactsCollector"/>), so a standard investigation silently had no session evidence and the "approved tool, unknown partner"
/// rule had nothing to read. This collector takes ONLY those logs, with the same copy rules; the other per-user artifacts (hives, Recycle Bin,
/// Notepad tabs) stay in "Completă". It is not in FullForensic, where <see cref="UserArtifactsCollector"/> already collects the same files.
/// </summary>
public sealed class RemoteToolLogsCollector : ICollector
{
    private readonly UserArtifactsCollector _inner;

    /// <param name="inner">The collector whose copy rules are used (tests pass one with fake profiles and folders).</param>
    public RemoteToolLogsCollector(UserArtifactsCollector? inner = null) => _inner = inner ?? new UserArtifactsCollector();

    public string Name => "RemoteToolLogs";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.Standard;

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct) =>
        ctx.IsElevated ? _inner.CollectRemoteToolLogsOnly(ctx, ct)
            : CollectorOutcome.NotAvailable("Jurnalele de sesiune AnyDesk / TeamViewer: necesită drepturi de administrator (profilurile altor utilizatori); nimic nu a fost colectat.", "profiluri, ProgramData, Program Files");
}
