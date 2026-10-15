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

/// <summary>
/// Reads the operating-system entries from the text of <c>bcdedit /enum all /v</c> (D12). Pure. Language-independent: section headers are localised ("Încărcător de pornire Windows",
/// "Windows-Startladeprogramm"), but the element names (identifier, device, path, osdevice, recoverysequence ...) are not, so a block is recognised by its elements.
/// </summary>
public static class BootEntries
{
    /// <summary>Description of an entry that boots a WinPE / RAM disk image which is not the Windows Recovery Environment linked to the current system.</summary>
    public const string ExtraRamdiskEntry = "intrare de pornire WinPE/ramdisk suplimentară";

    private sealed record Block(string Header, Dictionary<string, string> El);

    private static List<Block> Parse(string bcdText)
    {
        var res = new List<Block>();
        foreach (var raw in System.Text.RegularExpressions.Regex.Split(bcdText.Replace("\r", ""), @"\n[ \t]*\n"))
        {
            var lines = raw.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) continue;
            var header = lines[0];
            int start = lines.Count > 1 && lines[1].Length >= 3 && lines[1].All(ch => ch == '-') ? 2 : (header.Contains(' ') && !header.StartsWith("identifier", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            var el = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in lines.Skip(start))
            {
                int sp = l.IndexOfAny([' ', '\t']);
                var name = sp < 0 ? l : l[..sp];
                var val = sp < 0 ? "" : l[sp..].Trim();
                el.TryAdd(name, val);   // the first occurrence wins; a repeated element name is not a second value
            }
            res.Add(new Block(start > 0 ? header : "", el));
        }
        return res;
    }

    private static string Get(Block b, string n) => b.El.TryGetValue(n, out var v) ? v : "";

    /// <summary>A boot loader (as opposed to the boot manager, the resume application, memory tests ...): it names a system device or a winload image.</summary>
    private static bool IsLoader(Block b)
    {
        var path = Get(b, "path").ToLowerInvariant();
        if (path.Contains("winresume")) return false;   // the hibernation resume application is not an operating system
        if (path.Contains("winload")) return true;
        if (b.El.ContainsKey("osdevice") || b.El.ContainsKey("systemroot")) return true;
        return b.Header.StartsWith("Windows Boot Loader", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWinreRamdisk(Block b) =>
        (Get(b, "device") + " " + Get(b, "osdevice")).Replace('/', '\\').Contains("\\Recovery\\WindowsRE\\Winre.wim", StringComparison.OrdinalIgnoreCase);

    private static bool IsRamdiskOrWinPe(Block b) =>
        (Get(b, "device") + " " + Get(b, "osdevice")).Contains("ramdisk=", StringComparison.OrdinalIgnoreCase) ||
        Get(b, "winpe").Equals("Yes", StringComparison.OrdinalIgnoreCase) || Get(b, "winpe").Equals("Da", StringComparison.OrdinalIgnoreCase) || Get(b, "winpe").Equals("Ja", StringComparison.OrdinalIgnoreCase) || Get(b, "winpe").Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The description of every real operating-system loader. Excluded: the loader the current system's <c>recoverysequence</c> points to, a loader whose RAM-disk image is
    /// \Recovery\WindowsRE\Winre.wim, and the hibernation resume application. A description or a "winpe" flag proves nothing (an attacker sets both), so any other ramdisk / WinPE
    /// loader counts and is named <see cref="ExtraRamdiskEntry"/>.
    /// </summary>
    public static List<string> OsLoaderDescriptions(string bcdText)
    {
        var blocks = Parse(bcdText);
        var loaders = blocks.Where(IsLoader).ToList();
        // the current system: {current}, else the boot manager's "default", else the first non-ramdisk loader
        var defaults = blocks.Select(b => Get(b, "default")).Where(d => d.Length > 0).ToList();
        var current = loaders.Where(b => Get(b, "identifier").Equals("{current}", StringComparison.OrdinalIgnoreCase) || defaults.Contains(Get(b, "identifier"), StringComparer.OrdinalIgnoreCase)).ToList();
        if (current.Count == 0 && loaders.FirstOrDefault(b => !IsRamdiskOrWinPe(b)) is { } first) current = [first];
        var recoveryIds = current.Select(b => Get(b, "recoverysequence")).Where(r => r.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var res = new List<string>();
        foreach (var b in loaders)
        {
            var id = Get(b, "identifier");
            if (recoveryIds.Contains(id) || IsWinreRamdisk(b) && !current.Contains(b)) continue;
            var d = Get(b, "description");
            if (d.Length == 0) d = "(fără descriere)";
            res.Add(IsRamdiskOrWinPe(b) ? $"{ExtraRamdiskEntry}: {d}" : d);
        }
        return res;
    }
}
