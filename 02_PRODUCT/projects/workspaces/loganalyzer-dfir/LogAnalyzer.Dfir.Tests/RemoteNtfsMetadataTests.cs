using System.Diagnostics;
using System.Text.Json;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The "ntfs-metadata" step of the remote package (T1-T6). NOTHING here runs the step: no shadow copy is ever created. The script text is checked,
/// and the raw-read function (Copy-NtfsMetadataRaw, cut out of the generated script) is run by Windows PowerShell against synthetic volume IMAGE FILES.
/// </summary>
[Collection("AppMode")]
public sealed class RemoteNtfsMetadataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ladfir_remntfs_" + Guid.NewGuid().ToString("N"));

    public RemoteNtfsMetadataTests()
    {
        AppModeContext.ResetForTests();
        AppModeContext.Initialize(new ModeDecision(AppMode.AirGapped, true, "test", ConnectivitySnapshot.Unknown("test")));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        AppModeContext.ResetForTests();
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase() => CaseWorkspace.Create(Path.Combine(_root, "case"), new CaseInfo { CaseId = "CASE-NTFS", Name = "ntfs", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
    private static string Corpus(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "Ntfs", n);

    private string Script()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", [RemoteCollection.NtfsMetadataArtifact]);
        return RemoteCollection.PackageScript(req, sha);
    }

    /// <summary>The text of the ntfs-metadata branch (between its elseif and the end of the artifact loop).</summary>
    private static string StepText(string script)
    {
        int a = script.IndexOf("elseif ($a -eq 'ntfs-metadata')", StringComparison.Ordinal);
        Assert.True(a > 0, "the script has no ntfs-metadata step");
        return script[a..script.IndexOf("$list = ", a, StringComparison.Ordinal)];
    }

    private static string FunctionText(string script)
    {
        int a = script.IndexOf("function Copy-NtfsMetadataRaw", StringComparison.Ordinal);
        Assert.True(a > 0);
        return script[a..script.IndexOf("foreach ($a in $Artifacts)", a, StringComparison.Ordinal)];
    }

    // ---------------- the script text ----------------

    [Fact]
    public void The_step_and_the_artifact_exist_and_the_script_is_accepted_by_PowerShell_5_1()
    {
        Assert.Contains("ntfs-metadata", RemoteCollection.KnownArtifacts);
        var script = Script();
        Assert.Contains("'ntfs-metadata'", script);
        var file = Path.Combine(_root, "syntax", "collect.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, script);
        var check = Path.Combine(_root, "syntax", "check.ps1");
        File.WriteAllText(check, "param([string]$f) $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); if ($e.Count -gt 0) { $e | ForEach-Object { $_.Message }; exit 1 }");
        var (code, text) = RunPs(check, file);
        Assert.True(code == 0, text);
    }

    [Fact]
    public void T1_to_T6_every_refusal_is_in_the_script()
    {
        var script = Script();
        var fn = FunctionText(script);
        var step = StepText(script);
        // T1: the device is opened for reading only, shared with everyone; the only writes go to the two files of the package folder
        Assert.Contains("[IO.File]::Open($device, 'Open', 'Read', 'ReadWrite')", fn);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(fn, @"\$device").Count);   // the parameter and that one open: nothing else touches the device
        Assert.DoesNotContain("'Write', 'ReadWrite'", fn);
        // T2: anchored regex, applied to the DeviceObject of the copy THIS step created (found by its ShadowID), before anything is opened
        // ^\\\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy\d+$ written with \\{2} for the two leading backslashes (a literal run of four trips the package-script "no UNC path" test)
        Assert.Contains(@"$ShadowDeviceRegex = '^\\{2}\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy\d+$'", script);
        var re = new System.Text.RegularExpressions.Regex(@"^\\{2}\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy\d+$");
        Assert.Matches(re, @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy12");
        Assert.DoesNotMatch(re, @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy");
        Assert.DoesNotMatch(re, @"\\?\GLOBALROOT\Device\HarddiskVolume3");
        Assert.DoesNotMatch(re, @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy12\x");
        Assert.DoesNotMatch(re, @"x\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy12");
        Assert.Contains("$_.ID -eq $made.ShadowID", step);
        Assert.Contains("$dev -notmatch $ShadowDeviceRegex", step);
        Assert.True(step.IndexOf("$ShadowDeviceRegex", StringComparison.Ordinal) < step.IndexOf("Copy-NtfsMetadataRaw $dev", StringComparison.Ordinal));
        Assert.DoesNotContain("Win32_ShadowCopy | Where-Object { $_.DeviceObject", step);
        Assert.Contains("$env:SystemDrive", step);
        // T3: boot sector
        Assert.Contains("@(512, 1024, 2048, 4096) -notcontains $bps", fn);
        Assert.Contains("($spc -band ($spc - 1)) -ne 0", fn);
        Assert.Contains("-$cpr -gt 16", fn);
        Assert.Contains("$rs -ne 1024 -and $rs -ne 4096", fn);
        Assert.Contains("$mftLcn -lt 0 -or $mftLcn -ge $clusters", fn);
        // T3: record 0, fixup (all positions before any restore), $DATA, flags, runs, sizes, $ATTRIBUTE_LIST
        int check = fn.IndexOf("fixup nepotrivit", StringComparison.Ordinal), restore = fn.IndexOf("$rec[$i * 512 - 2] = $rec[$uo", StringComparison.Ordinal);
        Assert.True(check > 0 && restore > check, "all sector ends are verified before the first restore");
        Assert.Contains("$rec[$p + 8] -ne 1", fn);                  // non-resident
        Assert.Contains("$rec[$p + 9] -eq 0", fn);                  // no name
        Assert.Contains("-band 0xC001", fn);                        // compressed / encrypted / sparse flags
        Assert.Contains("$lv -lt 1 -or $lv -gt 8", fn);
        Assert.Contains("if ($lo -eq 0) { throw 'run sparse", fn);
        Assert.Contains("$lo -gt 8", fn);
        Assert.Contains("$cur -lt 0 -or $cur -ge $clusters", fn);
        Assert.Contains("$lens.Count -ge 4096", fn);
        Assert.Contains("$sumClusters * $cs -lt $realSize", fn);
        Assert.Contains("$realSize -gt 16GB -or $realSize -gt $volBytes", fn);
        Assert.Contains("ToInt64($rec, $p + 56) -ne $realSize", fn);
        Assert.Contains("$t -eq 0x20", fn);
        Assert.Contains("colectati $MFT cu aplicatia LogAnalyzer pe statie", fn);
        // T4: the copy is deleted in finally, with the vssadmin fallback (same pattern as search-index)
        int fin = step.IndexOf("} finally {", StringComparison.Ordinal);
        Assert.True(fin > 0);
        Assert.Contains("Remove-CimInstance", step[fin..]);
        Assert.Contains("vssadmin.exe delete shadows", step[fin..]);
        Assert.True(step.IndexOf("Copy-NtfsMetadataRaw $dev", StringComparison.Ordinal) < fin);
        // T5: free space (size + 256 MiB) is read and compared before the first output file is created
        int free = fn.IndexOf("AvailableFreeSpace", StringComparison.Ordinal), create = fn.IndexOf("'CreateNew'", StringComparison.Ordinal);
        Assert.True(free > 0 && free < create);
        Assert.Contains("$realSize + 512 + 256MB -gt $free", fn);
        Assert.Contains("[IO.Path]::Combine($dir, '$MFT')", fn);      // literal path, .NET API
        Assert.Contains("[IO.Path]::Combine($dir, '$Boot')", fn);
        // T6: a short read is an error; partial files are removed on any error
        Assert.Contains("citire scurta", fn);
        Assert.Contains("if ($r -le 0) { throw", fn);
        Assert.Contains("Remove-Item -LiteralPath $mftOut -Force", fn);
        Assert.Contains("Remove-Item -LiteralPath $bootOut -Force", fn);
        // the hash computed during the copy is compared with the hash of the file as written
        Assert.Contains("(Get-Sha256Hex $mftOut) -ne $mftHex", fn);
        Assert.Contains("TransformBlock", fn);
        Assert.DoesNotContain("File]::Copy($root", fn);
    }

    // ---------------- the raw read, on synthetic volume images ----------------

    private const int Bps = 512, Spc = 8, Cs = Bps * Spc, Clusters = 32, RecLcn = 4, Run2Lcn = 20, RealSize = 16384;   // 16 records of 1,024: the smallest $MFT the script accepts

    private sealed class Opt
    {
        public Action<byte[]>? Boot; public Action<byte[]>? Rec; public bool BreakLastFixup; public bool AttrList; public int Truncate = -1;
        public byte[] Runs = [0x11, 0x02, 0x04, 0x11, 0x02, 0x10, 0x00, 0x00];
        public int DataNameLen; public byte NonResident = 1; public ushort DataFlags; public long Valid = RealSize, Real = RealSize;
    }

    /// <summary>A 32-cluster NTFS-like volume: boot at 0; record 0 (1,024 bytes) at cluster 4; $MFT data = clusters 4-5 then 20-21.</summary>
    private static byte[] Image(Opt o)
    {
        var img = new byte[Clusters * Cs];
        new Random(7).NextBytes(img);
        var boot = new byte[512];
        "NTFS    "u8.CopyTo(boot.AsSpan(3));
        BitConverter.GetBytes((ushort)Bps).CopyTo(boot, 11); boot[13] = Spc;
        BitConverter.GetBytes((long)(Clusters * Spc)).CopyTo(boot, 40);
        BitConverter.GetBytes((long)RecLcn).CopyTo(boot, 48);
        boot[64] = 0xF6;   // -10 -> 1,024
        boot[510] = 0x55; boot[511] = 0xAA;
        o.Boot?.Invoke(boot);
        var rec = new byte[1024];
        "FILE"u8.CopyTo(rec);
        BitConverter.GetBytes((ushort)0x30).CopyTo(rec, 4); BitConverter.GetBytes((ushort)3).CopyTo(rec, 6);
        BitConverter.GetBytes((ushort)1).CopyTo(rec, 22); BitConverter.GetBytes(1024).CopyTo(rec, 28);
        int p = 0x38; BitConverter.GetBytes((ushort)p).CopyTo(rec, 20);
        if (o.AttrList) { BitConverter.GetBytes(0x20).CopyTo(rec, p); BitConverter.GetBytes(24).CopyTo(rec, p + 4); p += 24; }
        BitConverter.GetBytes(0x80).CopyTo(rec, p);
        int len = 0x40 + o.Runs.Length; BitConverter.GetBytes(len).CopyTo(rec, p + 4);
        rec[p + 8] = o.NonResident; rec[p + 9] = (byte)o.DataNameLen; BitConverter.GetBytes(o.DataFlags).CopyTo(rec, p + 12);
        BitConverter.GetBytes(2L).CopyTo(rec, p + 24); BitConverter.GetBytes((ushort)0x40).CopyTo(rec, p + 32);
        BitConverter.GetBytes(4L * Cs).CopyTo(rec, p + 40); BitConverter.GetBytes(o.Real).CopyTo(rec, p + 48); BitConverter.GetBytes(o.Valid).CopyTo(rec, p + 56);
        o.Runs.CopyTo(rec, p + 0x40);
        BitConverter.GetBytes(0xFFFFFFFF).CopyTo(rec, p + len);
        o.Rec?.Invoke(rec);
        // fixup: the two sector ends hold the update sequence number, the original (zero) bytes sit in the array
        BitConverter.GetBytes((ushort)0xBEEF).CopyTo(rec, 0x30);
        BitConverter.GetBytes((ushort)0xBEEF).CopyTo(rec, 510);
        BitConverter.GetBytes((ushort)(o.BreakLastFixup ? 0xDEAD : 0xBEEF)).CopyTo(rec, 1022);
        boot.CopyTo(img, 0);
        rec.CopyTo(img, RecLcn * Cs);
        return o.Truncate >= 0 ? img[..o.Truncate] : img;
    }

    private static byte[] ExpectedMft(byte[] img)
    {
        var all = img.AsSpan(RecLcn * Cs, 2 * Cs).ToArray().Concat(img.AsSpan(Run2Lcn * Cs, 2 * Cs).ToArray()).ToArray();
        return all[..RealSize];
    }

    private static (int Code, string Text) RunPs(string file, params string[] args)
    {
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file }.Concat(args)) psi.ArgumentList.Add(a);
        psi.Environment.Remove("PSModulePath");
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd(); var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o + e);
    }

    /// <summary>Runs Copy-NtfsMetadataRaw (as cut out of the generated script) on every image of the folder; one "name|OK|note" or "name|ERR|message" per image.</summary>
    private Dictionary<string, (string Verdict, string Text, string OutDir)> RunAll(Dictionary<string, byte[]> images, bool readOnlyImages = true)
    {
        var scriptFile = Path.Combine(_root, "pkg-script.ps1");
        File.WriteAllText(scriptFile, Script());
        var imgDir = Path.Combine(_root, "images"); Directory.CreateDirectory(imgDir);
        foreach (var (n, b) in images)
        {
            var f = Path.Combine(imgDir, n + ".img"); File.WriteAllBytes(f, b);
            if (readOnlyImages) File.SetAttributes(f, FileAttributes.ReadOnly);   // a write handle on the "device" would fail: proves T1
        }
        var harness = Path.Combine(_root, "harness.ps1");
        File.WriteAllText(harness, """
            param($scriptFile, $imgDir, $outRoot)
            $ast = [Management.Automation.Language.Parser]::ParseFile($scriptFile, [ref]$null, [ref]$null)
            $names = 'Get-Sha256Hex', 'Read-NtfsBytes', 'Copy-NtfsMetadataRaw'
            foreach ($f in $ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $names -contains $n.Name }, $true)) { Invoke-Expression $f.Extent.Text }
            foreach ($img in Get-ChildItem -LiteralPath $imgDir -Filter *.img) {
                $out = Join-Path $outRoot $img.BaseName; New-Item -ItemType Directory -Path $out -Force | Out-Null
                try { $note = Copy-NtfsMetadataRaw $img.FullName $out; Write-Output ($img.BaseName + '|OK|' + $note) }
                catch { Write-Output ($img.BaseName + '|ERR|' + ($_.Exception.Message -replace '\s+', ' ')) }
            }
            """);
        var outRoot = Path.Combine(_root, "out");
        var (code, text) = RunPs(harness, scriptFile, imgDir, outRoot);
        Assert.True(code == 0, text);
        var d = new Dictionary<string, (string, string, string)>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Split('|').Length >= 3))
        {
            var parts = line.Split('|', 3);
            d[parts[0]] = (parts[1], parts[2], Path.Combine(outRoot, parts[0]));
        }
        Assert.Equal(images.Count, d.Count);
        return d;
    }

    [Fact]
    public void A_good_volume_is_copied_with_matching_hashes_and_a_read_only_image_proves_the_device_is_never_written()
    {
        var img = Image(new Opt());
        var r = RunAll(new() { ["good"] = img })["good"];
        Assert.True(r.Verdict == "OK", r.Text);
        Assert.Contains("16384 octeti, 2 run-uri, inregistrare de 1024 octeti", r.Text);
        Assert.Equal(ExpectedMft(img), File.ReadAllBytes(Path.Combine(r.OutDir, "$MFT")));
        Assert.Equal(img[..512], File.ReadAllBytes(Path.Combine(r.OutDir, "$Boot")));
        Assert.Equal(["$Boot", "$MFT"], Directory.GetFiles(r.OutDir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(img, File.ReadAllBytes(Path.Combine(_root, "images", "good.img")));   // the image is untouched
    }

    public static IEnumerable<object[]> Hostile() =>
    [
        ["bps513", new Opt { Boot = b => BitConverter.GetBytes((ushort)513).CopyTo(b, 11) }, "bytes/sector"],
        ["spc3", new Opt { Boot = b => b[13] = 3 }, "sectoare/cluster"],
        ["spc0", new Opt { Boot = b => b[13] = 0 }, "sectoare/cluster"],
        ["rec2048", new Opt { Boot = b => b[64] = 0xF5 }, "marimea inregistrarii"],
        ["rec2pow17", new Opt { Boot = b => b[64] = 0xEF }, "marimea inregistrarii"],
        ["lcn_out", new Opt { Boot = b => BitConverter.GetBytes(99L).CopyTo(b, 48) }, "in afara volumului"],
        ["lcn_neg", new Opt { Boot = b => BitConverter.GetBytes(-1L).CopyTo(b, 48) }, "in afara volumului"],
        ["no_55aa", new Opt { Boot = b => b[510] = 0 }, "55AA"],
        ["fixup_last", new Opt { BreakLastFixup = true }, "fixup nepotrivit"],
        ["resident", new Opt { NonResident = 0 }, "rezident"],
        ["named_data", new Opt { DataNameLen = 4 }, "nerezident fara nume"],
        ["sparse_flag", new Opt { DataFlags = 0x8000 }, "comprimat, criptat sau sparse"],
        ["compressed", new Opt { DataFlags = 0x0001 }, "comprimat, criptat sau sparse"],
        ["encrypted", new Opt { DataFlags = 0x4000 }, "comprimat, criptat sau sparse"],
        ["sparse_run", new Opt { Runs = [0x01, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00] }, "sparse"],
        ["l_nine", new Opt { Runs = [0x91, 0x02, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] }, "offset de 9 octeti"],
        ["v_zero", new Opt { Runs = [0x10, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00] }, "lungimea de 0 octeti"],
        ["run_beyond", new Opt { Runs = [0x11, 0x02, 0x04, 0x11, 0x01, 0x7F, 0x00, 0x00] }, "in afara volumului"],
        ["first_run_not_boot_lcn", new Opt { Runs = [0x11, 0x03, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00] }, "primul run"],
        ["valid_ne_real", new Opt { Valid = 9000 }, "valid data length"],
        ["real_gt_sum", new Opt { Real = 20480, Valid = 20480 }, "mai putin decat marimea reala"],
        ["real_gt_volume", new Opt { Real = 1L << 40, Valid = 1L << 40 }, "peste 16 GiB"],
        ["attr_list", new Opt { AttrList = true }, "colectati $MFT cu aplicatia LogAnalyzer pe statie"],
        ["short_read", new Opt { Truncate = Run2Lcn * Cs + 100 }, "citire scurta"],
        ["overlap", new Opt { Runs = [0x11, 0x02, 0x04, 0x11, 0x02, 0x00, 0x00, 0x00] }, "suprapuse"],
        ["run_over_cluster0", new Opt { Runs = [0x11, 0x02, 0x04, 0x11, 0x02, 0xFC, 0x00, 0x00] }, "clusterul 0"],
        ["real_1_byte", new Opt { Real = 1, Valid = 1 }, "multiplu"],
        ["real_not_multiple", new Opt { Real = 16384 + 512, Valid = 16384 + 512 }, "multiplu"],
        ["real_under_16_records", new Opt { Real = 8192, Valid = 8192 }, "multiplu"],
        ["not_ntfs", new Opt { Boot = b => b[3] = (byte)'X' }, "nu este NTFS"],
    ];

    [Fact]
    public void Every_hostile_volume_is_refused_with_its_own_reason_and_leaves_no_file_behind()
    {
        var cases = Hostile().ToDictionary(c => (string)c[0], c => ((Opt)c[1], (string)c[2]));
        var results = RunAll(cases.ToDictionary(c => c.Key, c => Image(c.Value.Item1)));
        var wrong = new List<string>();
        foreach (var (name, (_, why)) in cases)
        {
            var r = results[name];
            if (r.Verdict != "ERR" || !r.Text.Contains(why, StringComparison.Ordinal)) wrong.Add($"{name}: {r.Verdict} {r.Text} (se aștepta „{why}”)");
            else if (Directory.GetFileSystemEntries(r.OutDir).Length != 0) wrong.Add($"{name}: refuzat dar au rămas fișiere: {string.Join(", ", Directory.GetFileSystemEntries(r.OutDir).Select(Path.GetFileName))}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void A_short_read_in_the_second_run_removes_the_part_already_written()
    {
        // the first run is read and written; the second hits the end of the (truncated) device: error, and the half-written $MFT and the $Boot are gone
        var r = RunAll(new() { ["cut"] = Image(new Opt { Truncate = Run2Lcn * Cs + 100 }) })["cut"];
        Assert.Equal("ERR", r.Verdict);
        Assert.Contains("citire scurta in run-ul 1", r.Text);
        Assert.Empty(Directory.GetFileSystemEntries(r.OutDir));
    }

    // ---------------- Verify + Import from a package built from the corpus ----------------

    private string CorpusPackage(RemoteCollectionRequest req, string requestSha, string stepStatus, string name, string collectedUtc, bool tamperMft = false, string startUtc = "2026-11-10T12:29:00.0000000Z", bool duplicate = false, bool noBootFile = false)
    {
        var pkg = Path.Combine(_root, name);
        var dir = Path.Combine(pkg, "files", "NTFS"); Directory.CreateDirectory(dir);
        File.Copy(Corpus("mft.bin"), Path.Combine(dir, "$MFT"));
        File.Copy(Corpus("boot.bin"), Path.Combine(dir, "$Boot"));
        string Sha(string p) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)));
        var files = Directory.GetFiles(dir).Select(p => new { path = Path.GetRelativePath(pkg, p).Replace('\\', '/'), size = new FileInfo(p).Length, sha256 = Sha(p) }).ToArray();
        if (duplicate) files = [.. files, files.Single(f => f.path.EndsWith("$MFT"))];
        if (noBootFile) { files = [.. files.Where(f => !f.path.EndsWith("$Boot"))]; File.Delete(Path.Combine(dir, "$Boot")); }   // "noBootFile" = a package without $Boot
        if (tamperMft) { var b = File.ReadAllBytes(Path.Combine(dir, "$MFT")); b[5000] ^= 0xFF; File.WriteAllBytes(Path.Combine(dir, "$MFT"), b); }
        var manifest = new
        {
            format = RemoteCollection.Format, requestId = req.RequestId, requestSha256 = requestSha, host = req.TargetHost, user = @"PC01\admin", elevated = true,
            collectedUtc,
            steps = new[] { new { artifact = "ntfs-metadata", startUtc, endUtc = "", status = stepStatus, error = stepStatus == "ok" ? "$MFT: 262144 octeti" : "disc plin" } },
            files, timezone = "UTC",
        };
        var mp = Path.Combine(pkg, "manifest.json");
        File.WriteAllText(mp, JsonSerializer.Serialize(manifest));
        File.WriteAllText(Path.Combine(pkg, "manifest.sha256"), Sha(mp) + "\n");
        return pkg;
    }

    [Fact]
    public void An_ok_package_imports_MFT_and_Boot_typed_in_one_folder_with_the_collection_time_and_MftParser_reads_it_like_the_corpus()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", [RemoteCollection.NtfsMetadataArtifact]);
        const string collected = "2026-11-10T12:30:00.0000000Z", started = "2026-11-10T12:29:00.0000000Z";
        var check = RemoteCollection.Verify(ws, CorpusPackage(req, sha, "ok", "pk_ok", collected, startUtc: started));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var items = RemoteCollection.Import(ws, check);
        var mft = items.Single(i => i.SourceType == MftParser.SourceTypeName);
        var boot = items.Single(i => i.SourceType == NtfsMetadataCollector.BootSourceType);
        var dirOf = (EvidenceItem i) => Path.GetDirectoryName(ws.FullPath(i.StoredPath))!;
        Assert.Equal(dirOf(mft), dirOf(boot));
        Assert.StartsWith("NTFS_", Path.GetFileName(dirOf(mft)));
        Assert.Equal("$MFT", Path.GetFileName(ws.FullPath(mft.StoredPath)));
        Assert.Equal("$Boot", Path.GetFileName(ws.FullPath(boot.StoredPath)));
        var when = DateTimeOffset.Parse(started, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(when, mft.AcquiredAtUtc);
        Assert.Equal(when, boot.AcquiredAtUtc);
        Assert.Contains(req.RequestId, mft.Notes);
        Assert.Contains(req.RequestId, boot.Notes);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Corpus("mft.bin")))), mft.Sha256, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(DateTimeOffset.Parse(collected, System.Globalization.CultureInfo.InvariantCulture), mft.AcquiredAtUtc);   // the moment of the shadow copy (step start), not the end of the package
        // the same events as the corpus file parsed directly (record size from the boot sector beside it in both cases)
        var direct = Path.Combine(_root, "direct"); Directory.CreateDirectory(direct);
        File.Copy(Corpus("mft.bin"), Path.Combine(direct, "$MFT")); File.Copy(Corpus("boot.bin"), Path.Combine(direct, "$Boot"));
        List<string> Project(string path, DateTimeOffset acquired, out ParseResult res)
        {
            var sink = new ListSink();
            res = new MftParser().Parse(new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "t", SourceType = MftParser.SourceTypeName, StoredPath = path, AcquiredAtUtc = acquired }, path, sink, default);
            return sink.Events.Select(e => $"{e.Time.Utc:o}|{e.Path}|{e.Summary}|{string.Join(",", e.Fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + "=" + f.Value))}").ToList();
        }
        var a = Project(Path.Combine(direct, "$MFT"), when, out var ra);
        var b = Project(ws.FullPath(mft.StoredPath), mft.AcquiredAtUtc, out var rb);
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
        Assert.Equal((ra.Status, ra.Records, ra.MalformedRecords, ra.Gaps.Count), (rb.Status, rb.Records, rb.MalformedRecords, rb.Gaps.Count));
    }

    [Fact]
    public void A_failed_step_never_makes_a_typed_pair_and_a_changed_file_is_refused()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", [RemoteCollection.NtfsMetadataArtifact]);
        // failed step: documented behavior = the files are still imported (custody keeps what the package carried) but only as plain "file" evidence,
        // so MftParser never reads them and no $MFT/$Boot pair is formed
        var bad = RemoteCollection.Verify(ws, CorpusPackage(req, sha, "failed", "pk_failed", "2026-11-10T12:30:00.0000000Z"));
        Assert.True(bad.Ok, string.Join("; ", bad.Problems));
        var items = RemoteCollection.Import(ws, bad);
        Assert.DoesNotContain(items, i => i.SourceType is MftParser.SourceTypeName or NtfsMetadataCollector.BootSourceType);
        Assert.Equal(2, items.Count(i => i.SourceType == "file"));

        // a file changed after the target computed its hash: Verify refuses and Import throws
        var tampered = RemoteCollection.Verify(ws, CorpusPackage(req, sha, "ok", "pk_tampered", "2026-11-10T12:30:00.0000000Z", tamperMft: true));
        Assert.False(tampered.Ok);
        Assert.Contains(tampered.Problems, p => p.Contains("$MFT") && p.Contains("SHA-256"));
        Assert.Throws<InvalidOperationException>(() => RemoteCollection.Import(ws, tampered));
    }

    private int Registered(CaseWorkspace ws) => ws.LoadEvidence().Count;

    [Fact]
    public void L5_an_unreadable_step_start_refuses_the_import_before_anything_is_registered()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", [RemoteCollection.NtfsMetadataArtifact]);
        var check = RemoteCollection.Verify(ws, CorpusPackage(req, sha, "ok", "pk_nostart", "2026-11-10T12:30:00.0000000Z", startUtc: "ieri"));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        int before = Registered(ws);
        Assert.Throws<InvalidOperationException>(() => RemoteCollection.Import(ws, check));
        Assert.Equal(before, Registered(ws));
    }

    [Fact]
    public void L2_with_an_ok_step_exactly_one_MFT_and_one_Boot_by_exact_name_or_the_import_is_refused_before_anything_is_registered()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", [RemoteCollection.NtfsMetadataArtifact]);
        var noBoot = RemoteCollection.Verify(ws, CorpusPackage(req, sha, "ok", "pk_noboot", "2026-11-10T12:30:00.0000000Z", noBootFile: true));
        Assert.True(noBoot.Ok, string.Join("; ", noBoot.Problems));
        int before = Registered(ws);
        var ex = Assert.Throws<InvalidOperationException>(() => RemoteCollection.Import(ws, noBoot));
        Assert.Contains("$Boot", ex.Message);
        Assert.Equal(before, Registered(ws));
    }

    [Fact]
    public void L3_a_file_listed_twice_in_the_manifest_fails_Verify_and_nothing_is_registered()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", [RemoteCollection.NtfsMetadataArtifact]);
        var dup = RemoteCollection.Verify(ws, CorpusPackage(req, sha, "ok", "pk_dup", "2026-11-10T12:30:00.0000000Z", duplicate: true));
        Assert.False(dup.Ok);
        Assert.Contains(dup.Problems, p => p.Contains("dublat") && p.Contains("$MFT"));
        int before = Registered(ws);
        Assert.Throws<InvalidOperationException>(() => RemoteCollection.Import(ws, dup));
        Assert.Equal(before, Registered(ws));
    }

    [Fact]
    public void L4_a_failed_Remove_CimInstance_falls_back_to_vssadmin_and_the_last_message_says_to_delete_by_hand()
    {
        var step = StepText(Script());
        int fin = step.IndexOf("} finally {", StringComparison.Ordinal);
        var f = step[fin..];
        int rem = f.IndexOf("Remove-CimInstance", StringComparison.Ordinal), vss = f.IndexOf("vssadmin.exe delete shadows \"/shadow=$($made.ShadowID)\"", StringComparison.Ordinal);
        Assert.True(rem > 0 && vss > rem, "vssadmin comes after Remove-CimInstance");
        Assert.Contains("catch", f[rem..vss]);                         // a Remove-CimInstance failure is caught, not thrown past the fallback
        Assert.DoesNotMatch(@"else", f[..vss]);   // not if/else: after a failed removal the fallback must still run
        Assert.Contains("nu a putut fi stearsa; stergeti-o manual", f);
    }

    [Fact]
    public void ImportKind_types_only_the_exact_NTFS_paths()
    {
        Assert.Equal(MftParser.SourceTypeName, RemoteCollection.ImportKind("files/NTFS/$MFT", "x"));
        Assert.Equal(NtfsMetadataCollector.BootSourceType, RemoteCollection.ImportKind("files/NTFS/$Boot", "x"));
        Assert.Equal("file", RemoteCollection.ImportKind("files/Other/$MFT", "x"));
        Assert.Equal("file", RemoteCollection.ImportKind("files/NTFS/$mft", "x"));
        Assert.Equal("file", RemoteCollection.ImportKind("files/NTFS/sub/$MFT", "x"));
    }
}
