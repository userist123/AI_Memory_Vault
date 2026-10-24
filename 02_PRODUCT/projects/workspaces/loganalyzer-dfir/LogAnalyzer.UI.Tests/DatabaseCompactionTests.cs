using System;
using System.IO;
using System.Linq;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Infrastructure.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>Senior review 2026-10-11: a cleared database kept its size (98.5 % empty pages). Clearing now gives the space back, and a mostly empty database is compacted.</summary>
    public class DatabaseCompactionTests
    {
        [Fact]
        public void Clearing_gives_the_space_back_and_a_small_database_is_left_alone()
        {
            var dir = Path.Combine(Path.GetTempPath(), "la-db-" + Guid.NewGuid().ToString("N"));
            try
            {
                var db = new DatabaseService(dir);
                db.InitializeDatabase();
                var big = new string('x', 4000);
                db.SaveEvents(Enumerable.Range(0, 3000).Select(i => new ParsedEvent { EventId = 4624, TimeCreated = DateTime.UtcNow, ProviderName = "p", MachineName = "m", Message = big + i }).ToList());
                var file = Directory.GetFiles(dir, "*.db").Single();
                var before = new FileInfo(file).Length;
                Assert.True(before > 5_000_000, $"fixture too small: {before}");
                Assert.Equal(0, db.CompactIfWasteful());                // full of data: nothing to compact
                db.ClearDatabase();
                Assert.True(new FileInfo(file).Length < before / 4, $"after clearing {new FileInfo(file).Length} of {before}");
                Assert.Equal(0, db.GetEventsCount(null, null, null));
            }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
    }
}
