using System.Runtime.InteropServices;
using System.Text;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.Dfir.Windows.Devices;

/// <summary>
/// Planner #11 item 3 (HG585 EXP-01 / MED-01): the medium an export was written to. Read-only: the drive type and the volume serial of the destination
/// (GetVolumeInformation, as <see cref="MediumReader"/> and the media register enrolment read it), then the ACTIVE entry of the verified signed media
/// register with that volume serial. Nothing is refused here; the result goes into the sealed audit detail of the export.
/// </summary>
public static class ExportMediumResolver
{
    /// <summary>
    /// <paramref name="activeRegister"/> returns the verified register in force, or null when none is usable (then the medium is "registru indisponibil",
    /// never "neînregistrat"). <paramref name="volume"/> is a seam for tests: (drive type, volume serial) of a path root.
    /// </summary>
    public static OperationAudit.ExportMedium Resolve(string fullPath, Func<SignedMediaRegisterDoc?> activeRegister, Func<string, (string Type, string Serial)>? volume = null)
    {
        var root = Path.GetPathRoot(fullPath) ?? "";
        if (root.Length == 0) return new(OperationAudit.MediumUnknown, "", OperationAudit.MediumUnknown);
        var (type, serial) = (volume ?? ReadVolume)(root);
        string medium;
        if (serial.Length == 0) medium = OperationAudit.MediumUnknown;
        else
        {
            SignedMediaRegisterDoc? doc;
            try { doc = activeRegister(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException) { doc = null; }
            if (doc is null) medium = OperationAudit.MediumRegisterUnavailable;
            else
            {
                var key = NormalizeVolumeSerial(serial);
                var hits = doc.Entries.Where(e => e.IsActive && NormalizeVolumeSerial(e.VolumeSerial) == key && key.Length > 0).Select(e => e.InventoryNumber.Trim()).Where(s => s.Length > 0).Distinct().ToList();
                // two active entries with one volume serial: name both, the register itself is ambiguous (its own check reports it)
                medium = hits.Count == 0 ? OperationAudit.MediumUnregistered : string.Join("|", hits);
            }
        }
        return new(type, serial, medium);
    }

    /// <summary>"1234-ABCD", "1234abcd" and "0x1234ABCD" are the same volume serial.</summary>
    public static string NormalizeVolumeSerial(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        return new string(t.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static (string Type, string Serial) ReadVolume(string root)
    {
        string type;
        try
        {
            type = new DriveInfo(root).DriveType switch
            {
                DriveType.Removable => "amovibil", DriveType.Fixed => "fix", DriveType.Network => "rețea", DriveType.CDRom => "optic", DriveType.Ram => "RAM", _ => OperationAudit.MediumUnknown,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { type = root.StartsWith(@"\\", StringComparison.Ordinal) ? "rețea" : OperationAudit.MediumUnknown; }
        var r = root.EndsWith('\\') ? root : root + "\\";
        return GetVolumeInformationW(r, null, 0, out var serial, out _, out _, null, 0) ? (type, serial.ToString("X8")) : (type, "");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder? label, int labelSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder? fsName, int fsNameSize);
}
