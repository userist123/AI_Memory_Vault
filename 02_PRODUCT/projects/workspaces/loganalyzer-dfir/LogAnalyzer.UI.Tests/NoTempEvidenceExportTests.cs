using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Security audit #14: the timeline CSV of a sealed case package is built in memory and the package is written straight into the destination
    /// zip; no staging folder with plaintext evidence is created in %TEMP%, after success or after an exception.
    /// </summary>
    public class NoTempEvidenceExportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_pkg_" + Guid.NewGuid().ToString("N"));
        public NoTempEvidenceExportTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        private static List<ParsedEvent> Events() => new()
        {
            new ParsedEvent { EventId = 4625, ProviderName = "Microsoft-Windows-Security-Auditing", MachineName = "LAB-PC", Message = "=cmd|' /C calc'!A0 logon failure",
                              TimeCreated = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc) },
        };

        [Fact]
        public void Plaso_csv_built_in_memory_equals_the_exported_file()
        {
            var svc = new SuperTimelineExportService();
            var file = Path.Combine(_dir, "t.csv");
            svc.ExportPlasoCsv(file, Events(), new List<ForensicArtifact>(), new List<RegistryArtifact>());
            Assert.Equal(File.ReadAllText(file), svc.BuildPlasoCsv(Events(), new List<ForensicArtifact>(), new List<RegistryArtifact>()));
        }

        private static (ConcurrentBag<string> Created, FileSystemWatcher W) WatchTemp()
        {
            var created = new ConcurrentBag<string>();
            var w = new FileSystemWatcher(Path.GetTempPath(), "DFIR_Case_*") { NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName, IncludeSubdirectories = false };
            w.Created += (_, e) => created.Add(e.FullPath);
            w.EnableRaisingEvents = true;
            return (created, w);
        }

        [Fact]
        public void Sealed_package_is_written_without_a_temp_staging_folder_and_its_manifest_matches()
        {
            var (created, w) = WatchTemp();
            var zip = Path.Combine(_dir, "case.zip");
            string sha;
            using (w)
            {
                sha = new DfirCasePackagingService().PackageAndSealCase(zip, "C1", "t", "org", "op", new List<ProvenanceLedgerEntry>(), "{}", "nis2", "message,datetime\r\nx,y\r\n");
                System.Threading.Thread.Sleep(200);
            }
            Assert.Empty(created);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant(), sha);
            using var z = ZipFile.OpenRead(zip);
            var names = z.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("SuperTimeline_Plaso_MACB.csv", names);
            Assert.Contains("MANIFEST.json", names);
            Assert.Contains("CERTIFICATE_OF_AUTHENTICITY.txt", names);
            using var m = JsonDocument.Parse(z.GetEntry("MANIFEST.json")!.Open());
            foreach (var f in m.RootElement.GetProperty("Files").EnumerateArray())
            {
                using var s = z.GetEntry(f.GetProperty("RelativePath").GetString()!)!.Open();
                using var ms = new MemoryStream(); s.CopyTo(ms);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant(), f.GetProperty("Sha256Hash").GetString());
            }
        }

        [Fact]
        public void A_failing_package_leaves_no_temp_folder_and_no_partial_zip()
        {
            var (created, w) = WatchTemp();
            var zip = Path.Combine(_dir, "missing_folder", "case.zip");
            using (w)
            {
                Assert.ThrowsAny<IOException>(() => new DfirCasePackagingService().PackageAndSealCase(zip, "C1", "t", "org", "op", new List<ProvenanceLedgerEntry>(), "{}", "nis2", "csv"));
                System.Threading.Thread.Sleep(200);
            }
            Assert.Empty(created);
            Assert.False(File.Exists(zip));
            Assert.Empty(Directory.GetFiles(_dir, "*", SearchOption.AllDirectories));
        }
    }
}
