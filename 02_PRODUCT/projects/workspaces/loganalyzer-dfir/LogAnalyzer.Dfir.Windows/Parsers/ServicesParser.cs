using System.Buffers.Binary;
using System.Globalization;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Services and drivers configured in the SYSTEM hive (current control set): image path, service DLL, start mode, type,
/// account. Configuration as saved, not proof of running (that is System 7036/7045 or the live snapshot).
/// </summary>
public sealed class ServicesParser : EvidenceParserBase
{
    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "ServicesParser", Version = "1.0", Artifact = "Hive SYSTEM — servicii și drivere (ControlSet curent)",
        SourceTypes = ["system_hive"], FileNames = ["SYSTEM", "SYSTEM.hiv", "HKLM_SYSTEM.hiv"], Fingerprints = ["regf"],
        SupportedOs = "Oricare (citire offline cu RawRegistry)",
        FormatVersions = ["Services\\<nume>: ImagePath, Type, Start, ObjectName, DisplayName, Parameters\\ServiceDll (Windows XP – 11)"],
        Limitations =
        [
            "Doar ControlSet-ul indicat de Select\\Current; seturile vechi nu sunt comparate.",
            "Ora este LastWriteTime al cheii serviciului (orice modificare a cheii, nu neapărat instalarea).",
            "ImagePath nu este expandat; DisplayName de forma @fișier.dll,-id nu este rezolvat.",
            "Starea de rulare nu există în hive.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "ServicesParserTests: SYSTEM.hiv real comparat cu WMI Win32_Service (nume, StartMode, StartName, PathName) și hive sintetic",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reg = new RawRegistry(fs);
        var current = reg.ReadValue("Select", "Current") is { Length: >= 4 } c ? BinaryPrimitives.ReadInt32LittleEndian(c) : 1;
        var servicesPath = $@"ControlSet{current:D3}\Services";
        var services = reg.OpenKey(servicesPath);
        if (services is null)
        {
            result.Gaps.Add(new EvidenceGap("Services", EvidenceStatus.NotAvailable, $"cheia {servicesPath} lipsește",
                "Configurația serviciilor nu este disponibilă", "System 7045, fotografia live", "Nu"));
            return;
        }
        // Prefetch configuration lives in the same hive; anti-forensics checks whether it was switched off.
        var prefetch = reg.OpenKey($@"ControlSet{current:D3}\Control\Session Manager\Memory Management\PrefetchParameters");
        if (prefetch?.Value("EnablePrefetcher") is { Data.Length: >= 4 } ep)
        {
            var v = BinaryPrimitives.ReadInt32LittleEndian(ep.Data).ToString(CultureInfo.InvariantCulture);
            sink.Add(new TimelineEvent
            {
                Time = Hive.KeyTime(prefetch), TimeSemantics = "key last written", Source = "SystemConfig", EvidenceId = item.EvidenceId,
                Summary = $"Prefetch: EnablePrefetcher = {v}", TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct,
                Confidence = Confidence.High, Locator = $@"SYSTEM\ControlSet{current:D3}\Control\Session Manager\Memory Management\PrefetchParameters\EnablePrefetcher",
                Fields = { ["EnablePrefetcher"] = v },
            });
        }
        foreach (var name in services.SubkeyNames)
        {
            ct.ThrowIfCancellationRequested();
            var keyPath = $@"{servicesPath}\{name}";
            var k = reg.OpenKey(keyPath);
            if (k is null) { result.MalformedRecords++; continue; }
            var image = k.Value("ImagePath")?.AsText ?? "";
            int type = Dword(k, "Type"), start = Dword(k, "Start");
            var serviceDll = reg.OpenKey(keyPath + @"\Parameters")?.Value("ServiceDll")?.AsText ?? k.Value("ServiceDll")?.AsText ?? "";
            var exe = CommandLine.Executable(image);
            sink.Add(new TimelineEvent
            {
                Time = Hive.KeyTime(k), TimeSemantics = "service key last written (any change to the key)",
                Source = "Service", EvidenceId = item.EvidenceId, Service = name, Path = exe, Process = Path.GetFileName(exe),
                User = k.Value("ObjectName")?.AsText ?? "",
                Summary = $"Serviciu {name} ({TypeName(type)}, {StartName(start)}): {image}" + (serviceDll.Length > 0 ? $"; ServiceDll {serviceDll}" : ""),
                TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High,
                Locator = $@"SYSTEM\{keyPath}",
                Fields =
                {
                    ["ImagePath"] = image, ["ServiceDll"] = serviceDll, ["DisplayName"] = k.Value("DisplayName")?.AsText ?? "",
                    ["ServiceType"] = TypeName(type), ["TypeValue"] = type.ToString(CultureInfo.InvariantCulture),
                    ["StartMode"] = StartName(start), ["StartValue"] = start.ToString(CultureInfo.InvariantCulture),
                    ["ControlSet"] = $"ControlSet{current:D3}",
                },
            });
            result.Records++;
        }
    }

    private static int Dword(RawKey k, string name) => k.Value(name)?.Data is { Length: >= 4 } d ? BinaryPrimitives.ReadInt32LittleEndian(d) : -1;

    public static string StartName(int start) => start switch
    {
        0 => "Boot", 1 => "System", 2 => "Auto", 3 => "Manual", 4 => "Disabled", -1 => "", _ => start.ToString(CultureInfo.InvariantCulture),
    };

    public static string TypeName(int type) => type switch
    {
        -1 => "",
        1 => "Kernel Driver",
        2 => "File System Driver",
        _ when (type & 0x10) != 0 && (type & 0x40) == 0 => "Own Process",
        _ when (type & 0x20) != 0 && (type & 0x40) == 0 => "Share Process",
        _ when (type & 0x40) != 0 => "User Service",
        _ => $"0x{type:X}",
    };

    /// <summary>
    /// ImagePath as the service control manager reports it (WMI PathName): the usual environment variables (anywhere, also
    /// inside quotes) and NT-path prefixes resolved for a station whose Windows folder is <paramref name="windowsDir"/>.
    /// Used to compare with live output, never to rewrite evidence.
    /// </summary>
    public static string AsScmPath(string image, string windowsDir)
    {
        var drive = windowsDir.Length >= 2 ? windowsDir[..2] : "C:";
        var vars = new (string Name, string Value)[]
        {
            ("%SystemRoot%", windowsDir), ("%windir%", windowsDir), ("%ProgramFiles%", drive + @"\Program Files"),
            ("%ProgramFiles(x86)%", drive + @"\Program Files (x86)"), ("%ProgramData%", drive + @"\ProgramData"), ("%SystemDrive%", drive),
        };
        var p = image.Trim();
        foreach (var (name, value) in vars) p = p.Replace(name, value, StringComparison.OrdinalIgnoreCase);
        if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) p = windowsDir + p[11..];
        else if (p.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase)) p = windowsDir + "\\" + p;
        else if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = p[4..];
        return p;
    }
}
