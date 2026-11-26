using System.Globalization;
using System.Management;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>One volume as the snapshot stores it (one JSON object, one line of the file; the parser quotes that line). A number that WMI did not give is null.</summary>
public sealed record BitLockerVolume(string DriveLetter, int? ProtectionStatus, int? ConversionStatus, int? EncryptionMethod, int? VolumeType, bool IsOsVolume);

/// <summary>Why the BitLocker namespace could not be read.</summary>
public enum BitLockerFailure { None, NamespaceMissing, AccessDenied, Other }

/// <summary>What the WMI read returns. <see cref="Volumes"/> null = not read, with the reason in <see cref="Error"/> and the kind in <see cref="Failure"/>; an empty list is "read, no volume".</summary>
public sealed record BitLockerSnapshot(string Machine, string SystemDrive, IReadOnlyList<BitLockerVolume>? Volumes, BitLockerFailure Failure = BitLockerFailure.None, string Error = "");

/// <summary>
/// Planner #14 E8: BitLocker / device encryption state of every volume, from WMI <c>root\CIMV2\Security\MicrosoftVolumeEncryption</c> class <c>Win32_EncryptableVolume</c>
/// (DriveLetter, ProtectionStatus, ConversionStatus, EncryptionMethod, VolumeType), read with System.Management, NOT with manage-bde (its text is localized). Saved as the JSON evidence
/// <c>bitlocker_volumes_json</c> (one object per volume on its own line, so the parser's quote is found as it is in the file). Needs elevation. A namespace that does not exist
/// (Windows Home without device encryption) or access denied saves nothing and is an error of the outcome with the reason, which the analysis turns into a gap, never into a
/// "not encrypted" finding. Quick, Standard and Full: one small WMI query.
/// </summary>
public sealed class BitLockerCollector : ICollector
{
    public const string SourceType = "bitlocker_volumes_json";
    public const string Namespace = @"root\CIMV2\Security\MicrosoftVolumeEncryption";

    private readonly Func<BitLockerSnapshot> _read;
    public BitLockerCollector() : this(null) { }
    /// <param name="read">The WMI read; tests pass a fake so nothing touches the real machine.</param>
    public BitLockerCollector(Func<BitLockerSnapshot>? read) => _read = read ?? ReadLive;

    public string Name => "BitLockerCollector";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.QuickAndUp;

    private static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var o = new CollectorOutcome { Source = "WMI " + Namespace, Tool = "System.Management (Win32_EncryptableVolume)" };
        if (!ctx.IsElevated) return CollectorOutcome.NotAvailable("Criptarea discului (BitLocker): nu a fost citită, necesită drepturi de administrator", "WMI");
        BitLockerSnapshot snap;
        try { snap = _read(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Security.SecurityException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            snap = new BitLockerSnapshot(Environment.MachineName, "", null, ex is UnauthorizedAccessException ? BitLockerFailure.AccessDenied : BitLockerFailure.Other, ex.Message);
        }
        ct.ThrowIfCancellationRequested();
        if (snap.Volumes is null)
        {
            string why = snap.Failure switch
            {
                BitLockerFailure.NamespaceMissing => $"spațiul de nume WMI {Namespace} nu există pe acest Windows (de exemplu ediția Home fără criptarea dispozitivului sau BitLocker neinstalat)",
                BitLockerFailure.AccessDenied => "acces refuzat la spațiul de nume WMI al criptării volumelor",
                _ => snap.Error.Length > 0 ? snap.Error : "citirea a eșuat fără un motiv raportat",
            };
            o.Errors.Add($"Criptarea discului (BitLocker): {why}; CFG-DISK-UNENCRYPTED nu poate fi evaluat" + (snap.Failure == BitLockerFailure.Other || snap.Error.Length == 0 ? "" : $" ({snap.Error})"));
            o.Status = EvidenceStatus.Failed;
            return o;
        }
        var dir = ctx.Case.RawDir("BitLocker");
        var path = Path.Combine(dir, $"bitlocker_volumes_{DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.json");
        File.WriteAllText(path, ToJson(snap, DateTimeOffset.UtcNow), new UTF8Encoding(false));
        o.Evidence.Add(ctx.Case.RegisterStored(path, "WMI Win32_EncryptableVolume", "BitLocker", SourceType, TemporalType.CurrentSnapshot, Name, Version));
        o.Status = EvidenceStatus.Success;
        return o;
    }

    /// <summary>The file: one object per line inside "volumes". UTF-8 without BOM.</summary>
    public static string ToJson(BitLockerSnapshot s, DateTimeOffset collectedUtc)
    {
        string J(object o) => JsonSerializer.Serialize(o, Compact);
        var sb = new StringBuilder("{\n");
        sb.Append("\"schema\":\"la.bitlocker/1\",\n");
        sb.Append("\"collectedUtc\":").Append(J(collectedUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))).Append(",\n");
        sb.Append("\"machine\":").Append(J(s.Machine)).Append(",\n");
        sb.Append("\"systemDrive\":").Append(J(s.SystemDrive)).Append(",\n");
        sb.Append("\"volumes\":[\n").Append(string.Join(",\n", (s.Volumes ?? []).Select(v => J(new
        {
            driveLetter = v.DriveLetter, protectionStatus = v.ProtectionStatus, conversionStatus = v.ConversionStatus, encryptionMethod = v.EncryptionMethod,
            volumeType = v.VolumeType, isOsVolume = v.IsOsVolume,
        })))).Append("\n]\n}\n");
        return sb.ToString();
    }

    // ---- the live read ------------------------------------------------------------------------------------------------

    public static BitLockerSnapshot ReadLive()
    {
        string machine = Environment.MachineName;
        string systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
        if (!OperatingSystem.IsWindows()) return new BitLockerSnapshot(machine, systemDrive, null, BitLockerFailure.Other, "nu este Windows");
        try
        {
            var scope = new ManagementScope(@"\\.\" + Namespace, new ConnectionOptions { Impersonation = ImpersonationLevel.Impersonate, EnablePrivileges = true });
            scope.Connect();
            var list = new List<BitLockerVolume>();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_EncryptableVolume"));
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    string letter = (mo["DriveLetter"] as string ?? "").Trim();
                    if (letter.Length == 0) continue;   // a volume without a drive letter is not shown by manage-bde -status either
                    int? prot = Number(mo, "ProtectionStatus") ?? Method(mo, "GetProtectionStatus", "ProtectionStatus");
                    int? conv = Number(mo, "ConversionStatus") ?? Method(mo, "GetConversionStatus", "ConversionStatus", precision: true);
                    int? meth = Number(mo, "EncryptionMethod") ?? Method(mo, "GetEncryptionMethod", "EncryptionMethod");
                    int? type = Number(mo, "VolumeType");
                    list.Add(new BitLockerVolume(letter, prot, conv, meth, type, letter.Equals(systemDrive, StringComparison.OrdinalIgnoreCase) || type == 0));
                }
            }
            return new BitLockerSnapshot(machine, systemDrive, list);
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass or ManagementStatus.NotFound)
        {
            return new BitLockerSnapshot(machine, systemDrive, null, BitLockerFailure.NamespaceMissing, ex.Message);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is ManagementException { ErrorCode: ManagementStatus.AccessDenied })
        {
            return new BitLockerSnapshot(machine, systemDrive, null, BitLockerFailure.AccessDenied, ex.Message);
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return new BitLockerSnapshot(machine, systemDrive, null, BitLockerFailure.Other, ex.Message);
        }
    }

    private static int? Number(ManagementObject mo, string name)
    {
        try { return mo[name] is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null; }
        catch (Exception ex) when (ex is ManagementException or FormatException or InvalidCastException or OverflowException) { return null; }
    }

    /// <summary>Some of the values are results of methods of the class (GetProtectionStatus, GetConversionStatus, GetEncryptionMethod) rather than properties.</summary>
    private static int? Method(ManagementObject mo, string method, string outName, bool precision = false)
    {
        try
        {
            ManagementBaseObject? input = null;
            if (precision) { input = mo.GetMethodParameters(method); input["PrecisionFactor"] = 0u; }
            using var result = mo.InvokeMethod(method, input, null);
            return result?[outName] is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null;
        }
        catch (Exception ex) when (ex is ManagementException or FormatException or InvalidCastException or OverflowException or System.Runtime.InteropServices.COMException) { return null; }
    }
}
