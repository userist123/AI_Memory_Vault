using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.7: a bounded static PE reader (header, sections, entropy, imports, imphash, overlay, signature presence) and explained indicators.</summary>
public sealed class PeTriageTests
{
    private static string System32(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);

    [Fact]
    public void A_real_windows_program_is_read_with_its_sections_imports_and_imphash()
    {
        var path = System32("notepad.exe");
        if (!File.Exists(path)) return;   // not a Windows test host
        var r = PeTriage.Analyze(path);
        Assert.True(r.IsPe, r.Problem);
        Assert.Equal(Environment.Is64BitOperatingSystem ? "x64" : "x86", r.Machine is "ARM64" ? (Environment.Is64BitOperatingSystem ? "x64" : "x86") : r.Machine);
        Assert.False(r.IsDll);
        Assert.Contains(r.Sections, s => s.Name == ".text" && s.Executable && !s.Writable);
        Assert.Contains(r.Imports, i => i.Dll.StartsWith("KERNEL32", StringComparison.OrdinalIgnoreCase) || i.Dll.StartsWith("api-ms-win", StringComparison.OrdinalIgnoreCase));
        Assert.Matches("^[0-9a-f]{32}$", r.ImpHash);
        Assert.Equal(64, r.Sha256.Length);
        Assert.DoesNotContain(r.Indicators, i => i.Code == "PE-WX-SECTION");
    }

    [Fact]
    public void A_dll_is_recognised_and_its_signature_presence_is_reported_without_a_verdict()
    {
        var path = System32("kernel32.dll");
        if (!File.Exists(path)) return;
        var r = PeTriage.Analyze(path);
        Assert.True(r.IsPe && r.IsDll, r.Problem);
        var unsigned = r.Indicators.FirstOrDefault(i => i.Code == "PE-UNSIGNED");
        if (unsigned is not null) Assert.Contains("nu înseamnă că fișierul este rău intenționat", unsigned.WhyItMatters);   // catalog-signed system files say so
    }

    [Fact]
    public void Data_appended_after_the_image_is_reported_as_an_overlay()
    {
        var path = System32("notepad.exe");
        if (!File.Exists(path)) return;
        var b = File.ReadAllBytes(path).Concat(new byte[20000]).ToArray();
        var r = PeTriage.Analyze(b, "notepad+overlay");
        Assert.True(r.IsPe);
        Assert.True(r.OverlaySize >= 20000);
        Assert.Contains(r.Indicators, i => i.Code == "PE-OVERLAY");
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x4D, 0x5A })]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0, 0, 0, 0 })]
    public void Not_a_pe_file_is_a_report_with_a_problem_never_an_exception(byte[] data)
    {
        var r = PeTriage.Analyze(data, "x");
        Assert.False(r.IsPe);
        Assert.NotEmpty(r.Problem);
    }

    [Fact]
    public void A_header_pointing_outside_the_file_and_random_mutations_never_throw()
    {
        var mz = new byte[0x80];
        mz[0] = (byte)'M'; mz[1] = (byte)'Z';
        BitConverter.GetBytes(int.MaxValue).CopyTo(mz, 0x3C);
        Assert.Contains("în afara fișierului", PeTriage.Analyze(mz, "x").Problem);

        var path = System32("notepad.exe");
        if (!File.Exists(path)) return;
        var original = File.ReadAllBytes(path);
        var rnd = new Random(20261108);
        for (int round = 0; round < 300; round++)
        {
            var b = (byte[])original.Clone();
            for (int k = 0; k < 24; k++) b[rnd.Next(0, Math.Min(b.Length, 4096))] = (byte)rnd.Next(256);   // headers and section table
            for (int k = 0; k < 8; k++) b[rnd.Next(0, b.Length)] = (byte)rnd.Next(256);                  // anywhere, import directory included
            var r = PeTriage.Analyze(b, "fuzz");
            Assert.True(r.IsPe || r.Problem.Length > 0);
        }
    }

    [Fact]
    public void Entropy_is_zero_for_constant_bytes_and_eight_for_uniform_bytes()
    {
        Assert.Equal(0, PeTriage.Entropy(new byte[1024], 0, 1024));
        var u = Enumerable.Range(0, 256 * 16).Select(i => (byte)(i % 256)).ToArray();
        Assert.Equal(8.0, PeTriage.Entropy(u, 0, u.Length), 6);
    }

    [Fact]
    public void An_executable_imported_into_a_case_is_triaged_by_the_pipeline_with_the_station_signature_check_and_read_back_on_open()
    {
        var src = System32("notepad.exe");
        if (!File.Exists(src)) return;
        var dir = Path.Combine(Path.GetTempPath(), "ladfir_pe_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, "suspect.exe");
            File.Copy(src, copy);
            var ws = InvestigationPipeline.NewCase(Path.Combine(dir, "cases"), "pe", TestScopes.Valid());
            InvestigationPipeline.Import(ws, [copy]);
            var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
            var row = Assert.Single(r.PeTriage);
            Assert.Equal("suspect.exe", row.OriginalName);
            Assert.True(row.Report.IsPe);
            Assert.NotEmpty(row.SignatureKind);
            Assert.True(File.Exists(Path.Combine(ws.Root, "Analysis", "pe_triage.json")));

            var loaded = CaseLoader.OpenAsync(ws.Root).GetAwaiter().GetResult().Case!;
            Assert.Equal(row.Report.Sha256, Assert.Single(loaded.Result.PeTriage).Report.Sha256);
            Assert.Equal(row.Report.Sections.Count, loaded.Result.PeTriage[0].Report.Sections.Count);
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_crafted_import_table_that_repeats_one_list_is_cut_and_said_never_a_million_functions()
    {
        // a minimal PE32: one section holding 512 descriptors that all point at the same 2048-entry lookup list
        var b = new byte[0x20000];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(b, 0x3C);
        "PE\0\0"u8.ToArray().CopyTo(b, 0x80);
        BitConverter.GetBytes((ushort)0x14C).CopyTo(b, 0x84);          // i386
        BitConverter.GetBytes((ushort)1).CopyTo(b, 0x86);              // one section
        BitConverter.GetBytes((ushort)224).CopyTo(b, 0x94);            // optional header size
        int opt = 0x98;
        BitConverter.GetBytes((ushort)0x10B).CopyTo(b, opt);
        BitConverter.GetBytes(16u).CopyTo(b, opt + 92);
        BitConverter.GetBytes(0x1000u).CopyTo(b, opt + 96 + 8);        // import directory RVA
        int sec = opt + 224;
        "data\0\0\0\0"u8.ToArray().CopyTo(b, sec);
        BitConverter.GetBytes(0x1F000u).CopyTo(b, sec + 8);            // virtual size
        BitConverter.GetBytes(0x1000u).CopyTo(b, sec + 12);            // RVA
        BitConverter.GetBytes(0x1F000u).CopyTo(b, sec + 16);           // raw size
        BitConverter.GetBytes(0x1000u).CopyTo(b, sec + 20);            // raw pointer
        uint list = 0x1000 + 512 * 20 + 20, name = list + 2049 * 4;
        for (int d = 0; d < 512; d++)
        {
            int e = 0x1000 + d * 20;
            BitConverter.GetBytes(list).CopyTo(b, e);
            BitConverter.GetBytes(name).CopyTo(b, e + 12);
        }
        for (int k = 0; k < 2048; k++) BitConverter.GetBytes(name).CopyTo(b, (int)list + k * 4);
        "evil.dll\0"u8.ToArray().CopyTo(b, (int)name);
        var r = PeTriage.Analyze(b, "crafted");
        Assert.True(r.IsPe, r.Problem);
        Assert.True(r.ImportsTruncated);
        Assert.True(r.Imports.Sum(i => i.Functions.Count) <= 10_000);
        Assert.Contains(r.Indicators, i => i.Code == "PE-IMPORTS-TRUNCATED");
    }
}
