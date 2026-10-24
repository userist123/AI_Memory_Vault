using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Privacy guard (docs/dfir/PRIVACY_OF_TEST_DATA.md). Reads a list of private identifiers (machine name, user name, SID prefix, serials,
    /// names from a real case) from a LOCAL file outside the repository and fails if any project file contains one of them.
    /// The list is never committed; when the file is absent (CI, another machine) the test returns and says so.
    /// </summary>
    public class PrivateIdentifiersGuardTests
    {
        private readonly ITestOutputHelper _out;
        public PrivateIdentifiersGuardTests(ITestOutputHelper output) => _out = output;

        private static readonly string[] TextExtensions =
        {
            ".cs", ".md", ".json", ".ps1", ".psm1", ".xaml", ".csproj", ".props", ".targets", ".slnx", ".bat", ".cmd", ".txt",
            ".xml", ".csv", ".yaml", ".yml", ".sh", ".manifest", ".resx", ".config", ".sigma", ".yar", ".yara", ".html", ".lic"
        };

        private static string Root()
        {
            var d = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(d, "LogAnalyzer.slnx"))) d = Path.GetDirectoryName(d)!;
            return d;
        }

        private static string ListPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ai-memory-vault", "private_identifiers.txt");

        [Fact]
        public void No_project_file_contains_a_private_identifier_from_the_local_list()
        {
            var list = Environment.GetEnvironmentVariable("LA_PRIVATE_IDENTIFIERS") is { Length: > 0 } custom ? custom : ListPath();
            if (!File.Exists(list))
            {
                _out.WriteLine("Private identifier list not found (" + list + "); guard skipped on this machine.");
                return;
            }

            var ids = File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length >= 3 && !l.StartsWith('#')).Distinct().ToList();
            _out.WriteLine("Checking " + ids.Count + " private identifiers.");
            if (ids.Count == 0) return;

            var root = Root();
            var hits = new List<string>();
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f);
                var parts = rel.Split(Path.DirectorySeparatorChar);
                if (parts.Any(p => p is "bin" or "obj" or ".vs" or ".git")) continue;
                if (!TextExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                string text;
                try { text = File.ReadAllText(f); } catch (IOException) { continue; }
                for (var i = 0; i < ids.Count; i++)
                    if (text.Contains(ids[i], StringComparison.OrdinalIgnoreCase))
                        hits.Add(rel + " (identifier #" + (i + 1) + ")");
            }

            Assert.True(hits.Count == 0, "private identifiers found in: " + string.Join("; ", hits));
        }
    }
}
