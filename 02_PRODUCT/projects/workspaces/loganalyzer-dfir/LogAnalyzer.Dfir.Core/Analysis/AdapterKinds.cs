namespace LogAnalyzer.Dfir.Analysis;

public enum AdapterKind { Physical, Wifi, Bluetooth, PhoneTethering, Cellular, UsbEthernet, Virtual }

/// <summary>Names the kind of a network adapter from its PnP class, hardware / compatible ids, service and description (D4). Pure; no system access.</summary>
public static class AdapterKinds
{
    private static readonly string[] RndisWords = ["rndis", "remote ndis", "usb8023", "usbnet", "internet sharing", "android", "iphone", "apple mobile device ethernet", "tethering"];
    private static readonly string[] CellularWords = ["mobile broadband", "wwan", "mbim", "cellular", "sierra wireless", "huawei mobile", "quectel"];
    private static readonly string[] UsbEthernetWords = ["usb ethernet", "usb gbe", "usb 10/100", "usb 2.0 ethernet", "usb3.0 ethernet", "usb gigabit", "ax88", "asix", "rtl815", "lan7", "usb lan", "usb-c ethernet", "cdc ecm", "cdc ncm"];

    /// <summary>"lte", "4g" and "5g" are too short to look for inside a word (Realtek contains "lte"): they count only as whole words.</summary>
    private static bool HasShortCellularWord(string text) =>
        text.ToLowerInvariant().Split([' ', '(', ')', '-', '_', '/', ',', '.'], StringSplitOptions.RemoveEmptyEntries).Any(t => t is "lte" or "4g" or "5g");

    /// <summary>The kind and the plain-Romanian reason it was chosen. <paramref name="virtualWords"/> is the list from the data file.</summary>
    public static (AdapterKind Kind, string Reason) Classify(string name, string instanceId, IEnumerable<string> compatibleIds, string service, string pnpClass, IEnumerable<string> virtualWords)
    {
        var compat = string.Join(" ", compatibleIds ?? []);
        var all = $"{name} {instanceId} {compat} {service}".ToLowerInvariant();
        bool usb = instanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) || compat.Contains("USB\\", StringComparison.OrdinalIgnoreCase);
        // RNDIS interface class: USB\Class_E0&SubClass_01&Prot_03 (wireless controller / RNDIS) or the generic Remote NDIS name; CDC-MBIM: Class_02&SubClass_0E.
        if (all.Contains("class_e0&subclass_01&prot_03") || RndisWords.Any(w => all.Contains(w)))
            return (AdapterKind.PhoneTethering, "adaptor RNDIS / partajare de internet prin USB: așa apare un telefon folosit ca modem (tethering)");
        if (all.Contains("class_02&subclass_0e") || CellularWords.Any(w => all.Contains(w)) || HasShortCellularWord($"{name} {service}"))
            return (AdapterKind.Cellular, "modem celular (WWAN / MBIM): conexiune la rețeaua de telefonie mobilă");
        bool virt = (virtualWords ?? []).Any(w => all.Contains(w.ToLowerInvariant())) || instanceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) || instanceId.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase);
        if (virt) return (AdapterKind.Virtual, "adaptor virtual (hipervizor, VPN sau tunel): nu este o ieșire fizică din sistem");
        if (all.Contains("bluetooth") || instanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) return (AdapterKind.Bluetooth, "radio Bluetooth");
        if (usb && (all.Contains("class_02&subclass_06") || all.Contains("class_02&subclass_0d") || UsbEthernetWords.Any(w => all.Contains(w)) || all.Contains("ethernet") || all.Contains("gbe") || all.Contains(" lan")))
            return (AdapterKind.UsbEthernet, "placă de rețea Ethernet conectată prin USB");
        if (all.Contains("wi-fi") || all.Contains("wifi") || all.Contains("wireless") || all.Contains("802.11") || all.Contains("wlan"))
            return (AdapterKind.Wifi, "adaptor Wi-Fi");
        return (AdapterKind.Physical, "adaptor de rețea fizic (integrat sau PCI)");
    }

    public static string Describe(AdapterKind k) => k switch
    {
        AdapterKind.PhoneTethering => "telefon folosit ca modem (RNDIS)",
        AdapterKind.Cellular => "modem celular (WWAN)",
        AdapterKind.UsbEthernet => "placă Ethernet pe USB",
        AdapterKind.Virtual => "adaptor virtual",
        AdapterKind.Wifi => "adaptor Wi-Fi",
        AdapterKind.Bluetooth => "radio Bluetooth",
        _ => "adaptor de rețea fizic",
    };
}

public enum HardwareKind { Camera, Microphone, Speaker, Sound }

/// <summary>Names what a camera / microphone / audio device is, from its PnP class and name (D15). Pure.</summary>
public static class HardwareKinds
{
    public static HardwareKind Of(HardwareFact h)
    {
        var n = h.Name.ToLowerInvariant();
        bool cls(string c) => h.PnpClass.Equals(c, StringComparison.OrdinalIgnoreCase);
        // class Image also holds scanners: only a name that says camera counts
        if (cls("Camera") || n.Contains("camera") || n.Contains("webcam") || n.Contains("cameră") || cls("Image") && n.Contains("cam")) return HardwareKind.Camera;
        if (n.Contains("microphone") || n.Contains("microfon") || n.StartsWith("mic ", StringComparison.Ordinal) || n.Contains("(mic") || n.Contains("line in") || n.Contains("stereo mix") || n.Contains("headset")) return HardwareKind.Microphone;
        if (cls("AudioEndpoint")) return HardwareKind.Speaker;
        return HardwareKind.Sound;
    }
}

/// <summary>Reads the operating-system entries from the text of <c>bcdedit /enum osloader</c> (D12). Pure.</summary>
public static class BootEntries
{
    /// <summary>The description of every Windows Boot Loader section (sections are separated by blank lines; the first line is the section title).</summary>
    public static List<string> OsLoaderDescriptions(string bcdText)
    {
        var res = new List<string>();
        foreach (var block in bcdText.Replace("\r", "").Split("\n\n"))
        {
            var lines = block.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0 || !(lines[0].StartsWith("Windows Boot Loader", StringComparison.OrdinalIgnoreCase) || lines[0].StartsWith("Înc", StringComparison.OrdinalIgnoreCase))) continue;
            if (IsRecoveryOrResume(lines)) continue;
            var d = lines.FirstOrDefault(l => l.StartsWith("description", StringComparison.OrdinalIgnoreCase));
            res.Add(d is null ? "(fără descriere)" : d["description".Length..].Trim() is { Length: > 0 } t ? t : "(fără descriere)");
        }
        return res;
    }

    /// <summary>The Windows Recovery Environment loader (RAM-disk boot of Winre.wim) and the hibernation resume loader exist on every station: they are not a second operating system.</summary>
    private static bool IsRecoveryOrResume(List<string> lines)
    {
        foreach (var l in lines)
        {
            var t = l.ToLowerInvariant();
            if (t.StartsWith("device", StringComparison.Ordinal) || t.StartsWith("osdevice", StringComparison.Ordinal))
                if (t.Contains("ramdisk=") || t.Contains("winre.wim")) return true;
            if (t.StartsWith("path", StringComparison.Ordinal) && (t.Contains("winresume.efi") || t.Contains("winresume.exe"))) return true;
            if (t.StartsWith("description", StringComparison.Ordinal) && (t.Contains("recovery environment") || t.Contains("mediul de recuperare") || t.Contains("resume from hibernate") || t.Contains("windows resume"))) return true;
            if (t.StartsWith("winpe", StringComparison.Ordinal) && t.EndsWith("yes", StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
