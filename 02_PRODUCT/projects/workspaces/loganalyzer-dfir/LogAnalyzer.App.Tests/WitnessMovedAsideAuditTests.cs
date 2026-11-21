using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Planner #9 item 3 (checkpoint 2026-11-18): an anti-rollback witness folder found without protection is moved aside and the witness starts again,
/// which silently hides an earlier rollback of the auth audit. The move is now written to the sealed auth audit and exposed for the sign-in window.
/// </summary>
public sealed class WitnessMovedAsideAuditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_witmove_" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    public WitnessMovedAsideAuditTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private (AuthService Svc, FileHeadWitness Witness) Start(HashSet<string> protectedDirs)
    {
        var authDir = Path.Combine(_dir, "auth");
        var witnessDir = Path.Combine(_dir, "witness");
        FileHeadWitness? fw = null;
        var security = new AuthStoreSecurity
        {
            CheckAcl = _ => StoreAclResult.Ok,
            ChainKey = _ => (_key, false),
            Witness = log => fw = new FileHeadWitness(witnessDir, log, d => protectedDirs.Contains(d), d => { Directory.CreateDirectory(d); protectedDirs.Add(d); }),
        };
        var svc = new AuthService(authDir, security: security);
        return (svc, fw!);
    }

    [Fact]
    public void An_unprotected_witness_folder_is_moved_aside_and_the_move_is_in_the_sealed_audit()
    {
        var planted = Path.Combine(_dir, "witness");
        Directory.CreateDirectory(planted);
        File.WriteAllText(Path.Combine(planted, "planted.witness"), "{}");

        var (svc, fw) = Start([]);

        Assert.NotNull(fw.MovedAside);
        Assert.Equal(fw.MovedAside, svc.WitnessMovedAside);
        Assert.Null(svc.WitnessMoveNotRecorded);
        var lines = File.ReadAllLines(svc.AuditPath).Where(l => l.Contains("auth.witness_moved_aside")).ToList();
        var line = Assert.Single(lines);
        Assert.Contains(Path.GetFileName(fw.MovedAside!), line);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
        Assert.False(string.IsNullOrWhiteSpace(AuthService.WitnessMovedMessage));
    }

    [Fact]
    public void A_protected_witness_folder_is_not_reported()
    {
        var witnessDir = Path.Combine(_dir, "witness");
        Directory.CreateDirectory(witnessDir);
        var (svc, fw) = Start(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { witnessDir });
        Assert.Null(fw.MovedAside);
        Assert.Null(svc.WitnessMovedAside);
        Assert.False(File.Exists(svc.AuditPath) && File.ReadAllText(svc.AuditPath).Contains("auth.witness_moved_aside"));
    }

    [Fact]
    public void The_move_is_audited_once_even_after_further_audited_operations()
    {
        var planted = Path.Combine(_dir, "witness");
        Directory.CreateDirectory(planted);
        var protectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (svc, _) = Start(protectedDirs);
        svc.AuditOperation(null, "test.op", "x");
        svc.AuditOperation(null, "test.op", "y");
        Assert.Single(File.ReadAllLines(svc.AuditPath), l => l.Contains("auth.witness_moved_aside"));
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void A_move_found_by_a_verification_alone_is_audited_at_once()
    {
        var protectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (svc, _) = Start(protectedDirs);
        svc.AuditOperation(null, "test.op", "x");
        protectedDirs.Clear();
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);   // the read moves the folder aside; no audited operation follows
        Assert.NotNull(svc.WitnessMovedAside);
        Assert.Single(File.ReadAllLines(svc.AuditPath), l => l.Contains("auth.witness_moved_aside"));
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void A_folder_that_loses_its_protection_during_the_session_is_reported_at_the_next_audited_operation()
    {
        var protectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (svc, fw) = Start(protectedDirs);
        svc.AuditOperation(null, "test.op", "x");          // creates the protected witness folder
        Assert.Null(svc.WitnessMovedAside);
        protectedDirs.Clear();                               // its DACL was changed meanwhile
        svc.AuditOperation(null, "test.op", "y");
        Assert.NotNull(svc.WitnessMovedAside);
        Assert.Single(File.ReadAllLines(svc.AuditPath), l => l.Contains("auth.witness_moved_aside"));
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void Two_moves_in_one_session_are_two_audit_lines()
    {
        // planner #10 item 5: the witness kept only the last move; two moves between checks lost the first one
        var protectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (svc, _) = Start(protectedDirs);
        svc.AuditOperation(null, "test.op", "x");
        protectedDirs.Clear();
        svc.AuditOperation(null, "test.op", "y");            // move 1 (the folder is recreated protected)
        protectedDirs.Clear();
        svc.AuditOperation(null, "test.op", "z");            // move 2
        var lines = File.ReadAllLines(svc.AuditPath).Where(l => l.Contains("auth.witness_moved_aside")).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(2, Directory.GetDirectories(_dir, "witness.untrusted-*").Length);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void A_leftover_untrusted_folder_from_an_earlier_start_is_audited_once_across_restarts()
    {
        // planner #10 item 5: folders moved aside by a build that did not audit moves (or before a crash) are found next to the witness folder at start
        var leftover = Path.Combine(_dir, "witness.untrusted-20201231235959-abcdef12");
        Directory.CreateDirectory(leftover);
        var protectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (svc, _) = Start(protectedDirs);
        Assert.Equal(leftover, svc.WitnessMovedAside);
        var (again, _) = Start(protectedDirs);                // next start: already named in the audit
        Assert.Null(again.WitnessMovedAside);
        var line = Assert.Single(File.ReadAllLines(again.AuditPath), l => l.Contains("auth.witness_moved_aside"));
        Assert.Contains("abcdef12", line);
        Assert.Equal(ChainStatus.Valid, again.VerifyAudit().Status);
    }
}
