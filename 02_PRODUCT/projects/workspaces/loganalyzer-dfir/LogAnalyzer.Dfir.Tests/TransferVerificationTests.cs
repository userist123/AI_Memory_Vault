using System.Text;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Research 2026-10-12 items 1 and 4: transfer verification by content and the quick check of an inserted stick (synthetic temp folders).</summary>
public class TransferVerificationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-tv-" + Guid.NewGuid().ToString("N"));

    public TransferVerificationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Make(string rel, string text)
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    private static string Sha(string text) => Hashing.Sha256(Encoding.UTF8.GetBytes(text));

    private static TransferRow Row(string nr, params (string Name, string Text)[] files) => new()
    {
        RegistrationNumber = nr, DateTime = "2026-10-12 10:00", Direction = "ieșire", Content = string.Join("; ", files.Select(f => f.Name)),
        Sha256 = string.Join(" ", files.Select(f => Sha(f.Text))),
    };

    [Fact]
    public void Identical_renamed_different_missing_and_unlisted_are_told_apart()
    {
        Make("a.txt", "alpha");
        Make("sub\\moved.txt", "beta");          // declared as b.txt, same content under another name
        Make("c.txt", "changed");                // declared c.txt with other content
        Make("extra.txt", "not declared");
        var row = Row("TR-1", ("a.txt", "alpha"), ("b.txt", "beta"), ("c.txt", "original"), ("d.txt", "gone"));

        var v = TransferVerifier.Verify([row], [row], _dir);

        Assert.Equal(DeclaredFileStatus.Identical, v.Declared.Single(d => d.Name == "a.txt").Status);
        var b = v.Declared.Single(d => d.Name == "b.txt");
        Assert.Equal(DeclaredFileStatus.Identical, b.Status);
        Assert.Contains("sub\\moved.txt", b.Where);
        Assert.Equal(DeclaredFileStatus.DifferentContent, v.Declared.Single(d => d.Name == "c.txt").Status);
        Assert.Equal(DeclaredFileStatus.NotFound, v.Declared.Single(d => d.Name == "d.txt").Status);
        Assert.Equal(["c.txt", "extra.txt"], v.Unlisted.Select(u => u.RelativePath).Order(StringComparer.Ordinal));   // the changed file matches no declared hash either
        Assert.Contains("CONȚINUT DIFERIT 1", v.SummaryText);
        Assert.Contains("NEGĂSIT", v.Declared.Single(d => d.Name == "d.txt").Text);
    }

    [Fact]
    public void A_file_locked_by_another_process_is_unreadable_not_missing()
    {
        var p = Make("locked.txt", "secret");
        var row = Row("TR-2", ("locked.txt", "something else"));
        using var hold = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var v = TransferVerifier.Verify([row], [row], _dir);
        Assert.Equal(DeclaredFileStatus.Unreadable, v.Declared.Single().Status);
        Assert.Contains("locked.txt", v.UnreadableOnMedium);
    }

    [Fact]
    public void Files_over_the_size_cap_are_not_hashed_and_the_count_cap_truncates()
    {
        Make("big.bin", new string('x', 5000));
        Make("s1.txt", "1"); Make("s2.txt", "2"); Make("s3.txt", "3");
        var row = Row("TR-3", ("big.bin", new string('x', 5000)));

        var capped = TransferVerifier.Verify([row], [row], _dir, new TransferVerifyOptions { MaxFileBytes = 1000 });
        Assert.Equal(DeclaredFileStatus.SameName, capped.Declared.Single().Status);      // name found, content not compared
        Assert.Contains("big.bin", capped.SkippedTooLarge);

        var few = TransferVerifier.Verify([row], [row], _dir, new TransferVerifyOptions { MaxFiles = 2 });
        Assert.True(few.Truncated);
        Assert.True(few.FilesScanned <= 2);
    }

    [Fact]
    public void Cancelling_stops_hashing_and_marks_the_result_partial()
    {
        Make("a.txt", "a"); Make("b.txt", "b");
        var row = Row("TR-4", ("a.txt", "a"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var v = TransferVerifier.Verify([row], [row], _dir, ct: cts.Token);
        Assert.True(v.Cancelled);
        Assert.Equal(0, v.FilesScanned);
        Assert.StartsWith("Verificare ÎNTRERUPTĂ", v.SummaryText);
    }

    [Fact]
    public void Verification_never_modifies_the_medium_and_writes_an_audit_entry()
    {
        var p = Make("a.txt", "alpha");
        var before = (File.GetLastWriteTimeUtc(p), Hashing.Sha256File(p), Directory.GetFileSystemEntries(_dir, "*", SearchOption.AllDirectories).Length);
        var row = Row("TR-5", ("a.txt", "alpha"));
        var v = TransferVerifier.Verify([row], [row], _dir);
        Assert.Equal(before, (File.GetLastWriteTimeUtc(p), Hashing.Sha256File(p), Directory.GetFileSystemEntries(_dir, "*", SearchOption.AllDirectories).Length));

        var reg = Path.Combine(_dir + "-reg", "transfers.json");
        try
        {
            Assert.True(TransferVerifier.WriteAudit(v, reg));
            var audit = RegisterStore.AuditPathFor(reg);
            Assert.Equal(ChainStatus.Valid, new HashChain(audit).Verify().Status);
            var text = File.ReadAllText(audit);
            Assert.Contains("transfer.verify", text);
            Assert.Contains("TR-5", text);
            Assert.Contains("IDENTIC", text);
        }
        finally { try { Directory.Delete(_dir + "-reg", true); } catch (IOException) { } }
    }

    // ---- inserted stick -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_clean_stick_raises_nothing()
    {
        Make("report.docx", "x"); Make("photos\\a.jpg", "y"); Make("notes.txt", "z");
        var r = RemovableMediaInspector.Inspect(_dir, "MYSTICK");
        Assert.False(r.IsSuspicious);
        Assert.Contains("nimic suspect", r.Text);
    }

    [Fact]
    public void Autorun_inf_and_executables_at_the_root_are_flagged()
    {
        Make("autorun.inf", "[autorun]"); Make("setup.exe", "MZ"); Make("run.vbs", "x"); Make("sub\\autorun.inf", "[autorun]");
        var r = RemovableMediaInspector.Inspect(_dir);
        Assert.Contains(r.Findings, f => f.Path == "autorun.inf");
        Assert.Contains(r.Findings, f => f.Path == "setup.exe");
        Assert.Contains(r.Findings, f => f.Path == "run.vbs");
        Assert.Contains(r.Findings, f => f.Path == "sub\\autorun.inf");
        Assert.True(r.IsSuspicious);
    }

    [Fact]
    public void Blank_named_hidden_and_recycler_folders_are_flagged_but_windows_own_folders_are_not()
    {
        Directory.CreateDirectory(Path.Combine(_dir, " "));                                    // looks empty in Explorer
        var hidden = Directory.CreateDirectory(Path.Combine(_dir, "Docs")); hidden.Attributes |= FileAttributes.Hidden;
        Directory.CreateDirectory(Path.Combine(_dir, "RECYCLER"));
        var svi = Directory.CreateDirectory(Path.Combine(_dir, "System Volume Information")); svi.Attributes |= FileAttributes.Hidden | FileAttributes.System;
        var r = RemovableMediaInspector.Inspect(_dir);
        Assert.Contains(r.Findings, f => f.Message.Contains("nume gol"));
        Assert.Contains(r.Findings, f => f.Message.Contains("ascuns") && f.Path == "Docs");
        Assert.Contains(r.Findings, f => f.Message.Contains("RECYCLER"));
        Assert.DoesNotContain(r.Findings, f => f.Path == "System Volume Information");
    }

    [Fact]
    public void A_lone_shortcut_named_like_the_volume_is_the_usb_worm_trick()
    {
        Make("MYSTICK.lnk", "L");
        var r = RemovableMediaInspector.Inspect(_dir, "MYSTICK");
        Assert.Contains(r.Findings, f => f.Message.Contains("vierme USB"));
        Make("other.lnk", "L");
        Assert.Contains(RemovableMediaInspector.Inspect(_dir, "MYSTICK").Findings, f => f.Path == "other.lnk" && !f.Message.Contains("vierme"));
    }

    [Fact]
    public void Junctions_are_not_followed()
    {
        var target = Directory.CreateDirectory(_dir + "-target");
        try
        {
            File.WriteAllText(Path.Combine(target.FullName, "autorun.inf"), "x");
            var link = Path.Combine(_dir, "link");
            try { Directory.CreateSymbolicLink(link, target.FullName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }      // no privilege to create a link here
            var r = RemovableMediaInspector.Inspect(_dir);
            Assert.DoesNotContain(r.Findings, f => f.Path.Contains("autorun.inf"));
            Assert.Contains(r.Findings, f => f.Path == "link");
        }
        finally { try { Directory.Delete(_dir + "-target", true); } catch (IOException) { } }
    }
}
