using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// USB storage devices from the SYSTEM hive: Enum\USBSTOR (vendor, product, revision, serial, friendly name) with the
/// device property times (first install 0064, last arrival 0066, last removal 0067), the VID/PID from Enum\USB and the
/// drive letter from MountedDevices.
/// </summary>
public sealed class UsbDevicesParser : EvidenceParserBase
{
    private const string DevPropTimes = "{83da6326-97a6-4088-9453-a1923f573b29}";

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "UsbDevicesParser", Version = "1.0", Artifact = "Hive SYSTEM — dispozitive de stocare USB (USBSTOR, USB, MountedDevices)",
        SourceTypes = ["system_hive"], FileNames = ["SYSTEM", "SYSTEM.hiv", "HKLM_SYSTEM.hiv"], Fingerprints = ["regf"],
        SupportedOs = "Oricare (citire offline cu RawRegistry)",
        FormatVersions = ["Enum\\USBSTOR + Properties\\{83da6326-…}\\0064/0066/0067 (Windows 8 – 11)", "Enum\\USB cu UASPStor (UAS) + Enum\\SCSI prin ContainerID", "MountedDevices (Unicode)"],
        Limitations =
        [
            "Doar ultima conectare și ultima deconectare per dispozitiv; conectările intermediare sunt în jurnalele Partition/Diagnostic și Kernel-PnP.",
            "Litera de unitate din MountedDevices este ultima asociere, nu neapărat cea de la momentul de interes.",
            "Utilizatorul care a folosit dispozitivul nu este în SYSTEM (vezi MountPoints2 din NTUSER).",
            "Dispozitivele MTP (telefoane) nu apar în USBSTOR.",
            "Un dispozitiv șters din Enum (de ex. de curățarea Plug and Play) rămâne doar ca identitate din MountedDevices, fără ore.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "UsbDevicesParserTests: SYSTEM.hiv real comparat cu jurnalul Partition/Diagnostic 1006 (seria, producătorul, ultima conectare) și hive sintetic",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reg = new RawRegistry(fs);
        int current = reg.ReadValue("Select", "Current") is { Length: >= 4 } c ? BinaryPrimitives.ReadInt32LittleEndian(c) : 1;
        var cs = $"ControlSet{current:D3}";
        var usbstor = $@"{cs}\Enum\USBSTOR";
        if (reg.OpenKey(usbstor) is null)
        {
            result.Gaps.Add(new EvidenceGap("USBSTOR", EvidenceStatus.Empty, $"cheia {usbstor} lipsește: niciun dispozitiv de stocare USB înregistrat sau cheia a fost ștearsă",
                "Nu se pot enumera stick-urile USB din registru", "Partition/Diagnostic 1006, Kernel-PnP 400/410, setupapi.dev.log", "Parțial"));
            return;
        }

        // VID/PID per serial from Enum\USB\VID_xxxx&PID_yyyy\<serial>.
        var vidPid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dev in reg.SubKeys($@"{cs}\Enum\USB"))
            foreach (var inst in reg.SubKeys(dev.Path))
                vidPid.TryAdd(Leaf(inst.Path), Leaf(dev.Path));

        // MountedDevices: values holding "_??_USBSTOR#Disk&...#<instance>#{...}" in UTF-16. \DosDevices\X: gives the drive
        // letter; any value keeps the device identity even after its Enum key is gone.
        var mounted = reg.OpenKey("MountedDevices");
        var letters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mountedDevices = new Dictionary<string, (string Class, string Value)>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in mounted?.Values ?? [])
        {
            if (v.Data.Length < 16) continue;
            var text = Encoding.Unicode.GetString(v.Data);
            int at = text.IndexOf("USBSTOR#", StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            var parts = text[at..].Split('#');
            if (parts.Length < 3) continue;
            mountedDevices.TryAdd(parts[2], (parts[1], v.Name));
            if (v.Name.StartsWith(@"\DosDevices\", StringComparison.OrdinalIgnoreCase)) letters[parts[2]] = v.Name[12..];
        }
        var seenInstances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cls in reg.SubKeys(usbstor))
        {
            ct.ThrowIfCancellationRequested();
            var (vendor, product, revision) = SplitClass(Leaf(cls.Path));
            foreach (var inst in reg.SubKeys(cls.Path))
            {
                var instance = Leaf(inst.Path);
                seenInstances.Add(instance);
                var serial = instance.EndsWith("&0", StringComparison.Ordinal) || instance.EndsWith("&1", StringComparison.Ordinal) ? instance[..^2] : instance;
                var friendly = inst.Value("FriendlyName")?.AsText ?? "";
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Vendor"] = vendor, ["Product"] = product, ["Revision"] = revision, ["Serial"] = serial, ["InstanceId"] = $@"USBSTOR\{Leaf(cls.Path)}\{instance}",
                    ["FriendlyName"] = friendly, ["VidPid"] = vidPid.GetValueOrDefault(serial, ""), ["DriveLetter"] = letters.GetValueOrDefault(instance, ""),
                    ["ContainerId"] = inst.Value("ContainerID")?.AsText ?? "",
                };
                fields["Interface"] = "USBSTOR";
                EmitTimes(reg, inst, $"{friendly} ({serial})", fields, item, sink, result);
            }
        }

        // USB Attached SCSI disks: Enum\USB instance driven by UASPStor; vendor/product from the Enum\SCSI disk with the same ContainerID.
        var scsiByContainer = new Dictionary<string, (string Vendor, string Product, string Revision)>(StringComparer.OrdinalIgnoreCase);
        foreach (var cls in reg.SubKeys($@"{cs}\Enum\SCSI"))
            foreach (var inst in reg.SubKeys(cls.Path))
                if (inst.Value("ContainerID")?.AsText is { Length: > 0 } cid)
                    scsiByContainer.TryAdd(cid, SplitClass(Leaf(cls.Path)));
        foreach (var dev in reg.SubKeys($@"{cs}\Enum\USB"))
            foreach (var inst in reg.SubKeys(dev.Path))
            {
                if (!string.Equals(inst.Value("Service")?.AsText, "UASPStor", StringComparison.OrdinalIgnoreCase)) continue;
                var serial = Leaf(inst.Path);
                var cid = inst.Value("ContainerID")?.AsText ?? "";
                var (vendor, product, revision) = scsiByContainer.GetValueOrDefault(cid);
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Vendor"] = vendor ?? "", ["Product"] = product ?? "", ["Revision"] = revision ?? "", ["Serial"] = serial,
                    ["InstanceId"] = $@"USB\{Leaf(dev.Path)}\{serial}", ["FriendlyName"] = $"{vendor} {product}".Trim(), ["VidPid"] = Leaf(dev.Path),
                    ["DriveLetter"] = "", ["ContainerId"] = cid, ["Interface"] = "UAS",
                };
                EmitTimes(reg, inst, $"{fields["FriendlyName"]} UAS ({serial})", fields, item, sink, result);
            }

        // Devices whose Enum key no longer exists but whose identity survives in MountedDevices (e.g. after Plug and Play cleanup).
        foreach (var (instance, (cls, valueName)) in mountedDevices.Where(m => !seenInstances.Contains(m.Key)))
        {
            var (vendor, product, revision) = SplitClass(cls);
            var serial = instance.EndsWith("&0", StringComparison.Ordinal) || instance.EndsWith("&1", StringComparison.Ordinal) ? instance[..^2] : instance;
            if (!seenInstances.Add(serial + "#mounted")) continue;     // "&0" and "&1" of the same device
            sink.Add(Event(item, Timestamp.Unknown(), "no time recorded (MountedDevices keeps the identity, not when)",
                $"USB {vendor} {product} ({serial}): cunoscut doar din MountedDevices; cheia din Enum\\USBSTOR nu mai există",
                $@"SYSTEM\MountedDevices\{valueName}",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Vendor"] = vendor, ["Product"] = product, ["Revision"] = revision, ["Serial"] = serial, ["InstanceId"] = $@"USBSTOR\{cls}\{instance}",
                    ["FriendlyName"] = $"{vendor} {product}".Trim(), ["VidPid"] = "", ["DriveLetter"] = letters.GetValueOrDefault(instance, ""), ["ContainerId"] = "",
                    ["Interface"] = "MountedDevices only",
                }));
            result.Records++;
        }
    }

    private static void EmitTimes(RawRegistry reg, RawKey inst, string label, Dictionary<string, string> fields, EvidenceItem item, IEventSink sink, ParseResult result)
    {
        var props = $@"{inst.Path}\Properties\{DevPropTimes}";
        var times = new (string Id, string Semantics, string Label)[]
        {
            ("0064", "USB device first install (DEVPKEY 0064)", "prima instalare"),
            ("0066", "USB device last arrival (DEVPKEY 0066)", "ultima conectare"),
            ("0067", "USB device last removal (DEVPKEY 0067)", "ultima deconectare"),
        };
        bool any = false;
        foreach (var (id, semantics, what) in times)
        {
            var data = reg.OpenKey($@"{props}\{id}")?.Value("")?.Data;
            if (data is not { Length: >= 8 }) continue;
            long ft = BinaryPrimitives.ReadInt64LittleEndian(data);
            fields[$"Time{id}Utc"] = ft > 0 ? DateTime.FromFileTimeUtc(ft).ToString("o", CultureInfo.InvariantCulture) : "";
            sink.Add(Event(item, Timestamp.FromFileTime(ft, $"DEVPKEY {id}"), semantics, $"USB {label}: {what}", $@"SYSTEM\{props}\{id}", fields));
            any = true;
            result.Records++;
        }
        if (!any)
        {
            sink.Add(Event(item, Hive.KeyTime(inst), "device instance key last written", $"USB {label}: înregistrat (fără ore de proprietăți)", $@"SYSTEM\{inst.Path}", fields));
            result.Records++;
        }
    }

    private static TimelineEvent Event(EvidenceItem item, Timestamp time, string semantics, string summary, string locator, Dictionary<string, string> fields) => new()
    {
        Time = time, TimeSemantics = semantics, Source = "USB", EvidenceId = item.EvidenceId, Summary = summary,
        TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High, Locator = locator,
        Fields = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase),
    };

    private static string Leaf(string path) => path[(path.LastIndexOf('\\') + 1)..];

    /// <summary>"Disk&amp;Ven_Kingston&amp;Prod_DataTraveler_3.0&amp;Rev_" → ("Kingston", "DataTraveler 3.0", "").</summary>
    public static (string Vendor, string Product, string Revision) SplitClass(string cls)
    {
        string Part(string prefix) => cls.Split('&').FirstOrDefault(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is { } p
            ? p[prefix.Length..].Replace('_', ' ').Trim() : "";
        return (Part("Ven_"), Part("Prod_"), Part("Rev_"));
    }
}
