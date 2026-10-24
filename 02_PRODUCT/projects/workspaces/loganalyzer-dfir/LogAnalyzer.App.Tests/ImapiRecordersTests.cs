using System.Diagnostics;
using LogAnalyzer.Dfir.Windows.Devices;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Comparison row D7: the optical writer capability read from the drive's own profile list through IMAPI2, next to WMI.</summary>
public sealed class ImapiRecordersTests
{
    [Fact]
    public void A_read_only_drive_has_no_writable_profiles()
    {
        // CD-ROM 0x08, DVD-ROM 0x10, BD-ROM 0x40
        Assert.Empty(ImapiRecorders.WritableProfiles([0x08, 0x10, 0x40]));
        Assert.False(new ImapiRecorder(["E:"], "Exemplu DVD-ROM", ImapiRecorders.WritableProfiles([0x08, 0x10])).CanWrite);
    }

    [Fact]
    public void A_writer_lists_its_recordable_profiles_once_each_in_order()
    {
        // DVD-RW restricted overwrite (0x13) and sequential (0x14) are both "DVD-RW"
        var p = ImapiRecorders.WritableProfiles([0x08, 0x09, 0x0A, 0x10, 0x13, 0x14, 0x1B, 0x43]);
        Assert.Equal(["CD-R", "CD-RW", "DVD-RW", "DVD+R", "BD-RE"], p);
        Assert.True(new ImapiRecorder(["E:"], "Exemplu DVD-RW", p).CanWrite);
    }

    [Fact]
    public void Reading_the_live_station_never_throws_and_respects_the_timeout()
    {
        var sw = Stopwatch.StartNew();
        var (recorders, problem) = ImapiRecorders.Read(TimeSpan.FromSeconds(20));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(25), $"took {sw.Elapsed}");
        // either a list (possibly empty: no optical drive) or a stated reason, never both missing
        Assert.True(recorders is not null || problem.Length > 0);
    }
}
