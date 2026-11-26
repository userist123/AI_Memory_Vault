using System.Buffers.Binary;
using LogAnalyzer.Dfir.IO;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Opt-in diagnostic on a real Amcache.hve: set LA_REAL_AMCACHE=&lt;path&gt;. Read-only; prints only counts and structure numbers, never value content.</summary>
public sealed class RealAmcacheDiagnosticTests(ITestOutputHelper output)
{
    public sealed class RealAmcacheFactAttribute : FactAttribute
    {
        public RealAmcacheFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LA_REAL_AMCACHE"))) Skip = "Set LA_REAL_AMCACHE=<path to Amcache.hve> to run";
        }
    }

    [RealAmcacheFact]
    public void Real_amcache_inventory_keys_all_have_values_read()
    {
        var path = Environment.GetEnvironmentVariable("LA_REAL_AMCACHE")!;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reg = new RawRegistry(fs);
        int total = 0, noValues = 0, noNamePath = 0;
        var shapes = new Dictionary<string, int>();
        var hist = new SortedDictionary<int, int>();
        foreach (var k in reg.SubKeys(@"Root\InventoryApplicationFile"))
        {
            total++;
            if (k.Values.Count == 0) noValues++;
            if ((k.Value("Name")?.AsText ?? "").Length == 0 && (k.Value("LowerCaseLongPath")?.AsText ?? "").Length == 0) noNamePath++;
            if (k.Values.Count == 2) { var sig = string.Join("+", k.Values.Select(v => $"{v.Name}({v.Type},{v.Data.Length}b)")); shapes[sig] = shapes.GetValueOrDefault(sig) + 1; }
            hist[k.Values.Count] = hist.GetValueOrDefault(k.Values.Count) + 1;
        }
        output.WriteLine($"keys={total} zeroValues={noValues} noNameAndPath={noNamePath}");
        output.WriteLine("valueCountHistogram: " + string.Join(", ", hist.Select(p => $"{p.Key}:{p.Value}")));
        foreach (var p in shapes) output.WriteLine($"2-value shape: {p.Key} x{p.Value}");
        Assert.Equal(0, noValues);
    }
}
