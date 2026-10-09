using LogAnalyzer.Dfir.Model;
using System.Reflection;
using System.Text;
using LogAnalyzer.Dfir.Windows.Acquisition;
using Microsoft.Data.Sqlite;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// `--self-test`: proves a published (self-contained, single-file) executable runs on a bare machine - no .NET installed, no
/// network, no AI. It loads every LogAnalyzer assembly shipped with the application, opens an encrypted SQLCipher database,
/// renders a PDF with the bundled QuestPDF natives and runs the deterministic investigation pipeline on a tiny built-in
/// corpus twice, requiring identical findings. Each line of the report is "OK name detail" or "FAIL name detail".
/// </summary>
public static class SelfTest
{
    public const string Switch = "--self-test";

    public sealed record Step(string Name, bool Ok, string Detail);

    public static bool IsRequested(IEnumerable<string> args) => args.Any(a => a.Equals(Switch, StringComparison.OrdinalIgnoreCase));

    /// <summary>Optional `--self-test-out=&lt;file&gt;`: the report is also written there (a WinExe has no console).</summary>
    public static string? OutputPath(IEnumerable<string> args) =>
        args.FirstOrDefault(a => a.StartsWith("--self-test-out=", StringComparison.OrdinalIgnoreCase))?["--self-test-out=".Length..];

    public static IReadOnlyList<Step> Run(string baseDirectory)
    {
        var steps = new List<Step>();
        void Do(string name, Func<string> body)
        {
            try { steps.Add(new Step(name, true, body())); }
            catch (Exception ex) { steps.Add(new Step(name, false, ex.GetType().Name + ": " + ex.Message)); }
        }

        Do("assemblies", () =>
        {
            var loaded = 0;
            foreach (var f in Directory.EnumerateFiles(baseDirectory, "LogAnalyzer*.dll"))
            {
                var asm = Assembly.LoadFrom(f);
                _ = asm.GetTypes();
                loaded++;
            }
            // Single-file: the assemblies are bundled, so also touch the ones this code already references.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name?.StartsWith("LogAnalyzer", StringComparison.Ordinal) == true))
            { _ = asm.GetTypes(); loaded++; }
            if (loaded == 0) throw new InvalidOperationException("no LogAnalyzer assembly could be loaded");
            return $"{loaded} assemblies loaded";
        });

        Do("sqlcipher", () =>
        {
            SQLitePCL.Batteries_V2.Init();
            var path = Path.Combine(Path.GetTempPath(), "la_selftest_" + Guid.NewGuid().ToString("N") + ".db");
            // No pooling: a pooled connection keeps the file open after Dispose, and Windows then refuses the header read and the delete.
            var cs = $"Data Source={path};Pooling=False";
            try
            {
                using (var c = new SqliteConnection(cs))
                {
                    c.Open();
                    using var k = c.CreateCommand(); k.CommandText = "PRAGMA key = 'self-test-key'"; k.ExecuteNonQuery();
                    using var v = c.CreateCommand(); v.CommandText = "PRAGMA cipher_version";
                    var version = v.ExecuteScalar() as string;
                    if (string.IsNullOrEmpty(version)) throw new InvalidOperationException("the bundled SQLite is not SQLCipher (cipher_version is empty)");
                    using var t = c.CreateCommand(); t.CommandText = "CREATE TABLE t(x TEXT); INSERT INTO t VALUES('ok')"; t.ExecuteNonQuery();
                }
                var head = new byte[16];
                using (var fs = File.OpenRead(path)) fs.ReadExactly(head);
                if (Encoding.ASCII.GetString(head).StartsWith("SQLite format 3", StringComparison.Ordinal)) throw new InvalidOperationException("database is not encrypted");
                using var c2 = new SqliteConnection(cs);
                c2.Open();
                using var k2 = c2.CreateCommand(); k2.CommandText = "PRAGMA key = 'self-test-key'"; k2.ExecuteNonQuery();
                using var r = c2.CreateCommand(); r.CommandText = "SELECT x FROM t";
                if ((string?)r.ExecuteScalar() != "ok") throw new InvalidOperationException("encrypted round trip failed");
                return "encrypted database created and read back";
            }
            finally { try { File.Delete(path); } catch (IOException) { } }
        });

        Do("pdf", () =>
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var bytes = Document.Create(d => d.Page(p => p.Content().Text("LogAnalyzer self-test"))).GeneratePdf();
            if (bytes.Length < 100 || Encoding.ASCII.GetString(bytes, 0, 4) != "%PDF") throw new InvalidOperationException("no PDF produced");
            return $"{bytes.Length} bytes";
        });

        Do("pipeline", () =>
        {
            var a = RunPipelineOnce();
            var b = RunPipelineOnce();
            if (a.Timeline == 0) throw new InvalidOperationException("the built-in corpus produced an empty timeline");
            if (a.Signature != b.Signature) throw new InvalidOperationException("two runs over the same corpus gave different findings");
            return $"{a.Timeline} timeline events, {a.Findings} findings, identical on the second run";
        });

        return steps;
    }

    public static string Format(IEnumerable<Step> steps) =>
        string.Join(Environment.NewLine, steps.Select(s => $"{(s.Ok ? "OK  " : "FAIL")} {s.Name} {s.Detail}")) + Environment.NewLine +
        (steps.All(s => s.Ok) ? "SELF-TEST PASSED" : "SELF-TEST FAILED") + Environment.NewLine;

    public static int Execute(IReadOnlyList<string> args, string baseDirectory)
    {
        var steps = Run(baseDirectory);
        var text = Format(steps);
        try { Console.Out.Write(text); } catch (IOException) { }
        if (OutputPath(args) is { Length: > 0 } p) File.WriteAllText(p, text, new UTF8Encoding(false));
        return steps.All(s => s.Ok) ? 0 : 1;
    }

    /// <summary>The findings of one run, reduced to a stable signature (ids and times differ between runs; rule, severity and text must not).</summary>
    public static (int Timeline, int Findings, string Signature) RunPipelineOnce(IEnumerable<string>? extraFiles = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "la_selftest_case_" + Guid.NewGuid().ToString("N"));
        try
        {
            var corpus = Path.Combine(root, "corpus");
            Directory.CreateDirectory(corpus);
            var task = Path.Combine(corpus, "orchestratormaintain");
            File.WriteAllBytes(task, BuiltInTask());
            var ws = InvestigationPipeline.NewCase(Path.Combine(root, "cases"), "self-test", new CaseScope
            {
                Purpose = "self-test (sintetic)", PeriodFromUtc = DateTimeOffset.UtcNow, PeriodToUtc = DateTimeOffset.UtcNow.AddHours(1),
                SystemsInScope = [Environment.MachineName], Approver = "self-test", LegalBasis = LegalBasis.Control,
                Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified,
            });
            InvestigationPipeline.Import(ws, new[] { task }.Concat(extraFiles ?? []));
            var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
            var sig = string.Join("\n", r.Findings.Select(f => $"{f.RuleId}|{f.Severity}|{f.Title}|{f.Description}").OrderBy(x => x, StringComparer.Ordinal));
            return (r.Timeline.Count, r.Findings.Count, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sig))));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    // A scheduled task that runs a hidden executable from a user-writable location at logon (UTF-16 LE with BOM, like System32\Tasks).
    private static byte[] BuiltInTask()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
            "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
            "  <RegistrationInfo><Date>2026-09-19T17:58:12.5+03:00</Date><Author>SYNTH-PC\\u</Author><URI>\\orchestratormaintain</URI></RegistrationInfo>\n" +
            "  <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>\n" +
            "  <Principals><Principal id=\"Author\"><UserId>S-1-5-21-1-2-3-1001</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
            "  <Settings><Hidden>true</Hidden><Enabled>true</Enabled></Settings>\n" +
            "  <Actions Context=\"Author\"><Exec><Command>C:\\Users\\u\\AppData\\Local\\Synth\\updater_core.exe</Command><Arguments>-run silent</Arguments></Exec></Actions>\n" +
            "</Task>";
        return [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(xml)];
    }
}
