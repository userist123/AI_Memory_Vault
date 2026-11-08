using System.Diagnostics;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Memory;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.6: the controlled Volatility 3 run. Each threat in <see cref="VolatilityRunner"/>'s model has a test here.</summary>
public sealed class VolatilityRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_vol_" + Guid.NewGuid().ToString("N"));
    private readonly string _tool, _symbols, _profileDir;
    private static readonly Func<string, LogAnalyzer.Dfir.Auth.StoreAclResult> Protected = _ => LogAnalyzer.Dfir.Auth.StoreAclResult.Ok;
    private readonly CaseWorkspace _ws;
    private readonly EvidenceItem _image;

    private const string PslistJson = """[{"PID": 4, "PPID": 0, "ImageFileName": "System", "CreateTime": "2026-11-07T09:00:00+00:00", "ExitTime": null, "Offset(V)": 1, "Threads": 100, "Handles": 0, "SessionId": null, "Wow64": false, "File output": "Disabled", "__children": []}]""";

    public VolatilityRunnerTests()
    {
        Directory.CreateDirectory(_dir);
        _tool = Path.Combine(_dir, "tool"); Directory.CreateDirectory(_tool);
        File.WriteAllBytes(Path.Combine(_tool, "vol.exe"), [0x4D, 0x5A, 1, 2, 3]);
        _profileDir = Path.Combine(_dir, "profile"); Directory.CreateDirectory(_profileDir);
        _symbols = Path.Combine(_dir, "symbols"); Directory.CreateDirectory(Path.Combine(_symbols, "windows"));
        File.WriteAllText(Path.Combine(_symbols, "windows", "ntkrnlmp.pdb.json"), "{\"symbols\": {}}");
        var img = Path.Combine(_dir, "mem.raw");
        File.WriteAllBytes(img, Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray());
        _ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "case"), "vol", new CaseScope
        {
            Purpose = "test", PeriodFromUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), PeriodToUtc = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            SystemsInScope = ["LAB"], Approver = "a", LegalBasis = LegalBasis.Incident, Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified,
        });
        _image = InvestigationPipeline.Import(_ws, [img]).Single();
    }

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private VolatilityToolProfile Profile(string? exe = null)
    {
        exe ??= Path.Combine(_tool, "vol.exe");
        return new()
        {
            Executable = exe, Sha256 = Hashing.Sha256File(exe),
            ToolManifestSha256 = FolderManifest.Create(Path.GetDirectoryName(exe)!, Path.Combine(_profileDir, VolatilityToolProfile.ToolManifestName)),
            SymbolsDir = _symbols, SymbolsManifestSha256 = FolderManifest.Create(_symbols, Path.Combine(_profileDir, VolatilityToolProfile.SymbolsManifestName)), TimeoutMinutes = 5,
        };
    }

    private VolatilityRunResult Run(IReadOnlyList<string> plugins, VolatilityToolProfile? p, VolatilityRunner.Launcher? launcher, string? evidenceId = null) =>
        VolatilityRunner.Run(_ws, evidenceId ?? _image.EvidenceId, plugins, p, null, default, launcher, _profileDir, Protected);

    private sealed class FakeTool
    {
        public readonly List<(IReadOnlyList<string> Args, ProcessStartInfo Psi)> Calls = [];
        public Func<string, string> Output = _ => PslistJson;
        public int Exit;
        public Action? During;

        public ToolRun Launch(string exe, IReadOnlyList<string> args, string logDir, string label, TimeSpan timeout, CancellationToken ct, Action<ProcessStartInfo> configure)
        {
            var psi = new ProcessStartInfo(exe);
            psi.Environment["PYTHONPATH"] = @"C:\evil"; psi.Environment["VOLATILITY_PLUGINS"] = @"C:\evil"; psi.Environment["PATH"] = "keep";
            configure(psi);
            Calls.Add((args, psi));
            During?.Invoke();
            Directory.CreateDirectory(logDir);
            var o = Path.Combine(logDir, label + Guid.NewGuid().ToString("N")[..6] + ".stdout.txt");
            var e = Path.ChangeExtension(o, ".stderr.txt");
            File.WriteAllText(o, Output(args[^1])); File.WriteAllText(e, "");
            var now = DateTimeOffset.UtcNow;
            return new ToolRun(exe, args, Exit, o, e, now, now.AddSeconds(2), false);
        }
    }

    [Fact]
    public void An_approved_tool_turns_each_plugin_output_into_evidence_derived_from_the_image_with_the_tool_in_its_custody()
    {
        var fake = new FakeTool();
        var r = Run(["windows.pslist", "windows.netscan"], Profile(), fake.Launch);
        Assert.True(r.Started, string.Join("; ", r.Refused));
        Assert.Equal(2, r.Plugins.Count(p => p.OutputEvidenceId.Length > 0));
        var (args, psi) = fake.Calls[0];
        Assert.Equal("--offline", args[0]);
        Assert.Equal(_ws.FullPath(_image.StoredPath), args[args.ToList().IndexOf("-f") + 1]);
        Assert.Equal("windows.pslist", args[^1]);
        Assert.False(psi.Environment.ContainsKey("PYTHONPATH"));
        Assert.False(psi.Environment.ContainsKey("VOLATILITY_PLUGINS"));
        Assert.Equal("keep", psi.Environment["PATH"]);
        Assert.Equal("cwd", Path.GetFileName(psi.WorkingDirectory));                                  // a private empty folder ...
        Assert.False(Directory.Exists(psi.WorkingDirectory));                                          // ... removed after the run, with the cache
        var outputs = _ws.LoadEvidence().Where(e => e.Collector == VolatilityRunner.Collector).ToList();
        Assert.All(outputs, e =>
        {
            Assert.Equal((VolatilityJsonParser.SourceTypeName, "derived", _image.EvidenceId), (e.SourceType, e.AcquisitionMethod, e.ParentEvidenceId));
            Assert.Contains(Profile().Sha256.ToLowerInvariant(), e.Notes);
            Assert.Contains("--offline", e.Notes);
            Assert.Contains("manifest unealtă", e.Notes);
        });
        var audit = File.ReadAllText(_ws.AppAuditLogPath);
        Assert.Contains("volatility.start", audit);
        Assert.Contains("volatility.done", audit);
    }

    [Fact]
    public void A_swapped_executable_a_plugin_off_the_list_or_a_missing_profile_starts_nothing()
    {
        var fake = new FakeTool();
        var p = Profile();
        File.WriteAllBytes(p.Executable, [0x4D, 0x5A, 9, 9, 9]);
        var r = Run(["windows.pslist"], p, fake.Launch);
        Assert.Contains(r.Refused, x => x.Contains("nu este cel aprobat"));
        var r2 = Run(["windows.dumpfiles"], Profile(), fake.Launch);
        Assert.Contains(r2.Refused, x => x.Contains("Plugin nepermis"));
        var r3 = Run(["windows.pslist"], null, fake.Launch);
        Assert.Contains(r3.Refused, x => x.Contains("nu este configurat"));
        var r4 = Run(["windows.pslist"], Profile(), fake.Launch, "EV-999999");
        Assert.Contains(r4.Refused, x => x.Contains("nu este în caz"));
        Assert.Empty(fake.Calls);
        Assert.Contains("volatility.refused", File.ReadAllText(_ws.AppAuditLogPath));
    }

    [Theory]
    [InlineData("added")]
    [InlineData("changed")]
    [InlineData("removed")]
    [InlineData("pin")]
    public void A_symbol_folder_that_is_not_the_approved_one_is_refused(string how)
    {
        var p = Profile();
        switch (how)
        {
            case "added": File.WriteAllText(Path.Combine(_symbols, "windows", "extra.json"), "{}"); break;
            case "changed": File.WriteAllText(Path.Combine(_symbols, "windows", "ntkrnlmp.pdb.json"), "{\"symbols\": {\"x\": 1}}"); break;
            case "removed": File.Delete(Path.Combine(_symbols, "windows", "ntkrnlmp.pdb.json")); break;
            default: p.SymbolsManifestSha256 = new string('0', 64); break;
        }
        var fake = new FakeTool();
        var r = Run(["windows.pslist"], p, fake.Launch);
        Assert.False(r.Started);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void The_image_and_the_executable_cannot_be_written_while_the_tool_runs()
    {
        var path = _ws.FullPath(_image.StoredPath);
        Exception? imageWrite = null, exeWrite = null;
        var fake = new FakeTool
        {
            During = () =>
            {
                File.SetAttributes(path, FileAttributes.Normal);
                imageWrite = Record.Exception(() => File.AppendAllText(path, "x"));
                exeWrite = Record.Exception(() => File.WriteAllBytes(Path.Combine(_tool, "vol.exe"), [0x4D, 0x5A, 7]));
            },
        };
        var r = Run(["windows.pslist"], Profile(), fake.Launch);
        Assert.IsType<IOException>(imageWrite);
        Assert.IsType<IOException>(exeWrite);
        Assert.Equal(_image.Sha256, Hashing.Sha256File(path));
        Assert.Single(r.Plugins, x => x.OutputEvidenceId.Length > 0);
    }

    [Fact]
    public void Error_exit_empty_and_non_array_outputs_are_not_kept()
    {
        var fake = new FakeTool { Output = plugin => plugin switch { "windows.pslist" => "", "windows.psscan" => "{\"a\": 1}", _ => "not json" } };
        var r = Run(["windows.pslist", "windows.psscan", "windows.netscan"], Profile(), fake.Launch);
        Assert.Equal(3, r.Plugins.Count(x => x.OutputEvidenceId.Length == 0 && x.Problem.Length > 0));
        var failing = new FakeTool { Exit = 1 };
        Assert.Contains("codul 1", Assert.Single(Run(["windows.pslist"], Profile(), failing.Launch).Plugins).Problem);
        Assert.DoesNotContain(_ws.LoadEvidence(), e => e.Collector == VolatilityRunner.Collector);
    }

    [Fact]
    public void A_real_process_starts_with_the_executable_pinned_and_its_error_exit_is_reported_not_kept()
    {
        // whoami.exe stands in for the tool: an approved, admin-only file that rejects Volatility's arguments with an error exit
        var own = Path.Combine(_dir, "realtool"); Directory.CreateDirectory(own);
        var whoami = Path.Combine(own, "whoami.exe");
        File.Copy(ToolRunner.System32("whoami.exe"), whoami);
        var r = VolatilityRunner.Run(_ws, _image.EvidenceId, ["windows.pslist"], Profile(whoami), null, default, null, _profileDir, Protected);
        Assert.True(r.Started, string.Join("; ", r.Refused));
        var run = Assert.Single(r.Plugins);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal("", run.OutputEvidenceId);
    }

    [Fact]
    public void A_profile_or_tool_folder_owned_by_an_ordinary_account_is_refused_with_the_real_rights_check()
    {
        // these temporary folders belong to the account running the tests, not to Administrators: exactly what a non-administrator could write
        var refused = VolatilityRunner.CheckTool(Profile(), null, _profileDir);
        Assert.Contains(refused, x => x.Contains("Folderul profilului uneltelor"));
        Assert.Contains(refused, x => x.Contains("Folderul uneltei"));
    }

    [Fact]
    public void A_file_planted_next_to_the_executable_is_refused()
    {
        var p = Profile();
        File.WriteAllBytes(Path.Combine(_tool, "python311.dll"), [1, 2, 3]);
        var fake = new FakeTool();
        var r = Run(["windows.pslist"], p, fake.Launch);
        Assert.Contains(r.Refused, x => x.Contains("python311.dll"));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void A_flooding_tool_is_stopped_at_the_output_limit_while_it_runs()
    {
        var big = Path.Combine(_dir, "big.txt");
        File.WriteAllText(big, new string('x', 2_000_000));
        var run = ToolRunner.Run(ToolRunner.System32("cmd.exe"), ["/c", "type", big], Path.Combine(_dir, "logs"), "flood", TimeSpan.FromMinutes(1), default, maxOutputBytes: 10_000);
        Assert.True(run.OutputLimitHit);
        Assert.True(new FileInfo(run.StdoutPath).Length <= 10_000);
        Assert.Contains("a depășit", VolatilityRunner.OutputProblem(run, run.StdoutPath));
    }
}