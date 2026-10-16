using System.Runtime.InteropServices;

namespace LogAnalyzer.Dfir.Windows.Devices;

/// <summary>One optical drive as Windows' disc-burning API (IMAPI2) sees it.</summary>
/// <param name="Drives">Drive letters of the recorder ("E:").</param>
/// <param name="Product">Vendor and product as the drive reports them.</param>
/// <param name="WritableProfiles">The writable disc profiles the drive supports (names such as "DVD+R"); empty = read-only drive.</param>
public sealed record ImapiRecorder(IReadOnlyList<string> Drives, string Product, IReadOnlyList<string> WritableProfiles)
{
    public bool CanWrite => WritableProfiles.Count > 0;
}

/// <summary>
/// Comparison row D7: the drive's own profile list read through IMAPI2 (MsftDiscMaster2 → MsftDiscRecorder2.SupportedProfiles), a second source next
/// to WMI Win32_CDROMDrive.Capabilities, which some drivers leave without "Supports Writing". Late-bound COM (no interop assembly), read-only: the
/// recorder is only initialised and queried, never acquired or written to. Runs on its own STA thread with a timeout, because a drive that spins
/// up a disc can hold the call.
/// </summary>
public static class ImapiRecorders
{
    // MMC profile numbers (IMAPI_PROFILE_TYPE) of recordable or rewritable media. [NESIGUR] list taken from the MMC-6 profile table as exposed by
    // IMAPI_PROFILE_TYPE; to confirm against a real writer's SupportedProfiles in the lab.
    private static readonly IReadOnlyDictionary<int, string> Writable = new Dictionary<int, string>
    {
        [0x09] = "CD-R", [0x0A] = "CD-RW", [0x11] = "DVD-R", [0x12] = "DVD-RAM", [0x13] = "DVD-RW", [0x14] = "DVD-RW", [0x15] = "DVD-R DL",
        [0x16] = "DVD-R DL", [0x1A] = "DVD+RW", [0x1B] = "DVD+R", [0x2A] = "DVD+RW DL", [0x2B] = "DVD+R DL", [0x41] = "BD-R", [0x42] = "BD-R",
        [0x43] = "BD-RE", [0x51] = "HD DVD-R", [0x52] = "HD DVD-RAM",
    };

    /// <summary>The names of the writable profiles among those a recorder reports, in order, without repeats.</summary>
    public static IReadOnlyList<string> WritableProfiles(IEnumerable<int> profiles) =>
        profiles.Where(Writable.ContainsKey).Select(p => Writable[p]).Distinct().ToList();

    /// <summary>The recorders IMAPI2 lists; null with the reason when IMAPI2 could not be used (service missing, COM error, timeout).</summary>
    public static (IReadOnlyList<ImapiRecorder>? Recorders, string Problem) Read(TimeSpan timeout)
    {
        IReadOnlyList<ImapiRecorder>? result = null;
        var problem = "";
        var t = new Thread(() =>
        {
            try { result = ReadCore(); }
            catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException)
            { problem = "IMAPI2 nu a putut fi folosit: " + ex.Message; }
        }) { IsBackground = true, Name = "imapi2-read" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        if (!t.Join(timeout)) return (null, $"IMAPI2 nu a răspuns în {timeout.TotalSeconds:0} s.");
        return (result, problem);
    }

    private static IReadOnlyList<ImapiRecorder>? ReadCore()
    {
        if (Type.GetTypeFromProgID("IMAPI2.MsftDiscMaster2") is not { } masterType || Type.GetTypeFromProgID("IMAPI2.MsftDiscRecorder2") is not { } recorderType)
            throw new COMException("IMAPI2 nu este înregistrat pe stație.");
        dynamic master = Activator.CreateInstance(masterType)!;
        var list = new List<ImapiRecorder>();
        try
        {
            if (!(bool)master.IsSupportedEnvironment) return list;
            int n = master.Count;
            for (int i = 0; i < n; i++)
            {
                string id = master[i];
                dynamic rec = Activator.CreateInstance(recorderType)!;
                try
                {
                    rec.InitializeDiscRecorder(id);
                    var drives = ((object[])rec.VolumePathNames).Select(v => v?.ToString()?.TrimEnd('\\') ?? "").Where(v => v.Length > 0).ToList();
                    var profiles = ((object[])rec.SupportedProfiles).Select(Convert.ToInt32).ToList();
                    list.Add(new ImapiRecorder(drives, $"{((string)rec.VendorId).Trim()} {((string)rec.ProductId).Trim()}".Trim(), WritableProfiles(profiles)));
                }
                finally { Marshal.FinalReleaseComObject(rec); }
            }
        }
        finally { Marshal.FinalReleaseComObject(master); }
        return list;
    }
}
