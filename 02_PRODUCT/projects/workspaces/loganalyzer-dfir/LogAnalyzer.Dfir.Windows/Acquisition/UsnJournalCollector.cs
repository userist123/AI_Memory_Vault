using System.Management;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// The NTFS change journal (USN) of every external NTFS volume connected at collection time, by a direct read of the journal (research 2026-11-07,
/// plan P1.4: until now the examiner had to run „fsutil usn readjournal” by hand, and fsutil loses Romanian letters when redirected, see
/// <see cref="UsnJournalNative"/>). With Removable Storage auditing it is the second independent source that a file was created or written on the medium;
/// without that auditing it is the only one. Read only: nothing is changed on the volume and a missing journal is NOT created. Never read: the system
/// volume, the volume that holds the case and the volume the application runs from (their journals would show the examiner's own work as media activity).
/// </summary>
public sealed class UsnJournalCollector : ICollector
{
    public string Name => "UsnJournalCollector";
    public string Version => "1.1";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.StandardAndUp;

    /// <summary>One volume as Windows reports it.</summary>
    public sealed record VolumeFact(string Letter, string FileSystem, bool Removable, bool OnUsbDisk);

    /// <summary>
    /// The volumes whose journal is read: NTFS on a removable drive or on a USB-attached disk, never one of <paramref name="excludedLetters"/> (system, case,
    /// application). Pure, for the tests.
    /// </summary>
    public static IReadOnlyList<string> Targets(IEnumerable<VolumeFact> volumes, params string[] excludedLetters)
    {
        var excluded = excludedLetters.Select(Letter).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return volumes.Where(v => v.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase) && (v.Removable || v.OnUsbDisk))
                      .Select(v => Letter(v.Letter))
                      .Where(l => l.Length == 1 && char.IsAsciiLetter(l[0]) && !excluded.Contains(l))
                      .Distinct().OrderBy(l => l, StringComparer.Ordinal).ToList();
    }

    /// <summary>„E”, from „E”, „E:”, „E:\” or a full path on E:; "" for a UNC path or nothing.</summary>
    public static string Letter(string? pathOrLetter)
    {
        var s = (pathOrLetter ?? "").Trim();
        if (s.Length == 0 || s.StartsWith(@"\\", StringComparison.Ordinal)) return "";
        if (s.Length >= 2 && s[1] == ':') return s[..1].ToUpperInvariant();
        return s.Length == 1 && char.IsAsciiLetter(s[0]) ? s.ToUpperInvariant() : "";
    }

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        const string how = "citire directă a jurnalului (FSCTL_READ_USN_JOURNAL), doar citire";
        if (!ctx.IsElevated) return CollectorOutcome.NotAvailable("Jurnalul USN al suporturilor externe: necesită drepturi de administrator", how);
        var o = new CollectorOutcome { Tool = "LogAnalyzer (FSCTL_READ_USN_JOURNAL)", ToolVersion = Version, Source = "suporturile NTFS externe conectate" };
        List<VolumeFact> volumes;
        try { volumes = Volumes(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            o.Status = EvidenceStatus.Failed;
            o.Errors.Add("Volumele conectate nu au putut fi listate: " + ex.Message);
            return o;
        }
        var targets = Targets(volumes, Environment.SystemDirectory, ctx.Case.Root, AppContext.BaseDirectory);
        if (targets.Count == 0)
        {
            o.Status = EvidenceStatus.Empty;
            o.Errors.Add("Niciun suport extern NTFS conectat la colectare (FAT/exFAT nu au jurnal USN; discul cazului și al aplicației nu se citesc).");
            return o;
        }
        var dir = ctx.Case.RawDir("USN");
        bool partial = false;
        foreach (var letter in targets)
        {
            ct.ThrowIfCancellationRequested();
            o.Commands.Add($"FSCTL_READ_USN_JOURNAL \\\\.\\{letter}:");
            // review round 1: anything one medium does (a crafted record, a driver error) stays with that medium and never stops the collection
            try
            {
                var read = UsnJournalNative.Read(letter[0], ct);
                var volume = VolumeIdentity(letter);
                var dest = Path.Combine(dir, $"usn_{letter}.csv");
                using (var w = new StreamWriter(new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None), new System.Text.UTF8Encoding(false)))
                    UsnJournalNative.WriteCsv(w, read.Info, read.Records, TimeZoneInfo.Local, volume, read.Incomplete);
                o.Evidence.Add(ctx.Case.RegisterStored(dest, $"{letter}: {volume} — jurnalul USN, citit direct (înregistrări v2/v3 convertite în formatul fsutil)",
                    "USN:" + letter, "usn_journal", TemporalType.Historical, Name, Version));
                if (!read.Complete) { partial = true; o.Errors.Add($"{letter}: {read.Incomplete}."); }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // 1179 = ERROR_JOURNAL_NOT_ACTIVE: the volume keeps no change journal (common on media formatted elsewhere)
                o.Errors.Add(ex.NativeErrorCode == 1179 ? $"{letter}: volumul nu are jurnal USN activ; nu s-a creat unul (ar fi modificat suportul)." : $"{letter}: {ex.Message} (cod {ex.NativeErrorCode})");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                o.Errors.Add($"{letter}: jurnalul nu a putut fi citit ({ex.GetType().Name}: {ex.Message}).");
            }
        }
        o.Status = o.Evidence.Count == 0 ? EvidenceStatus.Failed : partial || o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }

    /// <summary>„volumul NTFS „ETICHETĂ”, seria 1A2B-3C4D” (the volume serial ties the journal to the medium, not only to the drive letter); "" when unreadable.</summary>
    private static string VolumeIdentity(string letter)
    {
        try
        {
            using var s = new ManagementObjectSearcher($"SELECT VolumeName, VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID='{letter}:'");
            foreach (ManagementObject d in s.Get())
                using (d)
                {
                    var serial = d["VolumeSerialNumber"] as string ?? "";
                    var name = d["VolumeName"] as string ?? "";
                    return $"volumul NTFS{(name.Length > 0 ? $" „{name}”" : "")}{(serial.Length == 8 ? $", seria {serial[..4]}-{serial[4..]}" : "")}";
                }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return "";
    }

    /// <summary>
    /// The mounted volumes, with whether their disk is attached over USB. An external HDD/SSD reports itself as a fixed drive, and a UASP enclosure as SCSI
    /// in Win32_DiskDrive (review round 1): the Storage namespace's BusType (7 = USB) is asked first, Win32_DiskDrive InterfaceType second.
    /// </summary>
    private static List<VolumeFact> Volumes()
    {
        var usbLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            using var disks = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Number FROM MSFT_Disk WHERE BusType=7"));
            foreach (ManagementObject disk in disks.Get())
                using (disk)
                {
                    using var parts = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT DriveLetter FROM MSFT_Partition WHERE DiskNumber={Convert.ToUInt32(disk["Number"])}"));
                    foreach (ManagementObject part in parts.Get())
                        using (part) if (part["DriveLetter"] is char c && char.IsAsciiLetter(c)) usbLetters.Add(c.ToString());
                }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidCastException or FormatException) { }
        try
        {
            using var disks = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_DiskDrive WHERE InterfaceType='USB'");
            foreach (ManagementObject disk in disks.Get())
                using (disk)
                {
                    var id = (disk["DeviceID"] as string ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
                    using var parts = new ManagementObjectSearcher($"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{id}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                    foreach (ManagementObject part in parts.Get())
                        using (part)
                        {
                            var pid = (part["DeviceID"] as string ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
                            using var logical = new ManagementObjectSearcher($"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{pid}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                            foreach (ManagementObject ld in logical.Get())
                                using (ld) if (ld["DeviceID"] is string l) usbLetters.Add(l.TrimEnd(':'));
                        }
                }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }   // removable drives are still found below

        var list = new List<VolumeFact>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                var letter = d.Name.TrimEnd(':', '\\');
                list.Add(new VolumeFact(letter, d.DriveFormat, d.DriveType == DriveType.Removable, usbLetters.Contains(letter)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }
}
