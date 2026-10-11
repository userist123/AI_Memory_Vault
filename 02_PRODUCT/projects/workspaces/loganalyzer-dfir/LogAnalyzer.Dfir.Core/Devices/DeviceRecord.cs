using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>What kind of device, in the words the controller uses.</summary>
public enum DeviceKind
{
    InternalDisk, UsbStorage, MemoryCard, Optical, PortableDevice, Bluetooth, NetworkDrive, VirtualDisk, Thunderbolt, Other,
}

/// <summary>
/// Every storage medium or connectable device the station knows about (owner 2026-10-11: „pentru mediile de stocare îmi trebuie toate datele; plus
/// HDD, USB-uri, dispozitive ce se pot conecta și prin alte metode … ceva viu ca la Endpoint Protector”). Read from Windows, never written to it.
/// Times are what Windows recorded (first install, last arrival, last removal); empty means Windows did not record it, not that it never happened.
/// </summary>
public sealed record DeviceRecord
{
    public required DeviceKind Kind { get; init; }
    public required string InstanceId { get; init; }
    public string Name { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Model { get; init; } = "";
    public string Serial { get; init; } = "";
    public string VidPid { get; init; } = "";
    public string Bus { get; init; } = "";
    public long? CapacityBytes { get; init; }
    public string DriveLetters { get; init; } = "";
    public string VolumeLabel { get; init; } = "";
    public string FileSystem { get; init; } = "";
    public string Encryption { get; init; } = "";
    public bool ConnectedNow { get; init; }
    public DateTimeOffset? FirstConnectedUtc { get; init; }
    public DateTimeOffset? LastConnectedUtc { get; init; }
    public DateTimeOffset? LastRemovedUtc { get; init; }
    public string Sources { get; init; } = "";
    /// <summary>Every connection Windows logged (Partition/Diagnostic 1006), newest first; Windows' own device record keeps only the last one.</summary>
    public IReadOnlyList<DateTimeOffset> Connections { get; init; } = [];
    public string ConnectionsText => Connections.Count == 0 ? "" : $"{Connections.Count} conectări în jurnal; ultimele: {string.Join(", ", Connections.Take(5).Select(c => c.ToLocalTime().ToString("yyyy-MM-dd HH:mm")))}";

    public string KindText => DeviceText.Kind(Kind);
    /// <summary>What a person recognises first: the label written on the medium, else make and model, else the Windows name.</summary>
    public string DisplayName => VolumeLabel.Length > 0 && !VolumeLabel.Contains('·') ? $"{VolumeLabel} ({(Model.Length > 0 ? Model : Name)})"
                               : $"{Manufacturer} {Model}".Trim() is { Length: > 0 } mm ? mm : Name.Length > 0 ? Name : KindText;
    public string CapacityText => CapacityBytes is long b && b > 0 ? DeviceText.Bytes(b) : "";
    public string ConnectedText => Kind == DeviceKind.Bluetooth ? (ConnectedNow ? "asociat cu stația" : "asociat anterior") : ConnectedNow ? "conectat acum" : "neconectat";
}

/// <summary>A device compared with the media register: what the controller has to know about it at a glance.</summary>
public enum RegisterMatch { Registered, NotRegistered, Withdrawn, Expired, NotApplicable }

public sealed record DeviceAssessment(DeviceRecord Device, RegisterMatch Match, string RegistrationNumber, string Verdict, string WhatToDo)
{
    public string MatchText => Match switch
    {
        RegisterMatch.Registered => "în registru",
        RegisterMatch.NotRegistered => "NU este în registru",
        RegisterMatch.Withdrawn => "retras / distrus în registru",
        RegisterMatch.Expired => "în registru, dar valabilitatea a expirat",
        _ => "nu se ține în registru",
    };
    public bool NeedsAttention => Match is RegisterMatch.NotRegistered or RegisterMatch.Withdrawn or RegisterMatch.Expired;
}

public static class DeviceText
{
    public static string Kind(DeviceKind k) => k switch
    {
        DeviceKind.InternalDisk => "Disc intern (HDD/SSD)",
        DeviceKind.UsbStorage => "Stocare USB (stick / disc extern)",
        DeviceKind.MemoryCard => "Card de memorie (SD/MMC)",
        DeviceKind.Optical => "Unitate CD/DVD/Blu-ray",
        DeviceKind.PortableDevice => "Telefon / dispozitiv portabil (MTP/PTP)",
        DeviceKind.Bluetooth => "Dispozitiv Bluetooth",
        DeviceKind.NetworkDrive => "Unitate de rețea (folder partajat)",
        DeviceKind.VirtualDisk => "Disc virtual (VHD/ISO montat)",
        DeviceKind.Thunderbolt => "Dispozitiv Thunderbolt / FireWire",
        _ => "Alt dispozitiv",
    };

    public static string Explain(DeviceKind k) => k switch
    {
        DeviceKind.InternalDisk => "Discul din interiorul stației. Seria lui identifică fizic discul; dacă se schimbă între controale, discul a fost înlocuit.",
        DeviceKind.UsbStorage => "Un stick sau un disc extern conectat prin USB. Pe o stație izolată sunt permise doar suporturile din registru.",
        DeviceKind.MemoryCard => "Un card de memorie (SD, microSD, MMC) citit de un cititor intern sau USB. Se tratează ca un stick.",
        DeviceKind.Optical => "Unitatea care citește sau scrie CD/DVD. Discurile scrise pot scoate date de pe stație.",
        DeviceKind.PortableDevice => "Un telefon, o cameră sau alt dispozitiv conectat prin cablu, care apare ca dispozitiv portabil (MTP/PTP). Poate copia fișiere fără să apară ca unitate cu literă.",
        DeviceKind.Bluetooth => "Un dispozitiv asociat prin Bluetooth (telefon, căști, tastatură). Un telefon asociat poate primi fișiere fără fir.",
        DeviceKind.NetworkDrive => "Un folder de pe alt calculator folosit ca unitate. Pe o stație izolată nu ar trebui să existe.",
        DeviceKind.VirtualDisk => "Un fișier imagine (VHD, VHDX, ISO) montat ca disc. Poate aduce sau scoate date fără un suport fizic.",
        DeviceKind.Thunderbolt => "Un dispozitiv conectat prin Thunderbolt sau FireWire; poate fi un disc extern sau un adaptor.",
        _ => "Un dispozitiv care poate stoca sau transfera date.",
    };

    public static string Bytes(long b) => b >= 1_000_000_000_000 ? $"{b / 1e12:0.#} TB" : b >= 1_000_000_000 ? $"{b / 1e9:0.#} GB" : b >= 1_000_000 ? $"{b / 1e6:0.#} MB" : $"{b} B";
}

/// <summary>Pure rules: which device a Windows instance id/class is, its serial and VID/PID, and how it compares with the media register.</summary>
public static class DeviceRules
{
    /// <summary>
    /// The kind of a device from its instance id (enumerator), setup class and hardware ids; null when it is not a storage/connectable device we
    /// track (keyboards, internal buses, Bluetooth service entries, volumes that belong to a disk we already list).
    /// </summary>
    public static DeviceKind? Classify(string instanceId, string setupClass, string hardwareIds, string name)
    {
        var id = instanceId.ToUpperInvariant();
        var cls = setupClass.ToUpperInvariant();
        var hw = (instanceId + " " + hardwareIds + " " + name).ToUpperInvariant();
        if (id.StartsWith(@"USBSTOR\")) return hw.Contains("SD") && (hw.Contains("MMC") || hw.Contains("CARD") || hw.Contains("SD/MMC")) ? DeviceKind.MemoryCard : DeviceKind.UsbStorage;
        if (id.StartsWith(@"SD\") || id.StartsWith(@"SDBUS\") || id.StartsWith(@"MMC\")) return DeviceKind.MemoryCard;
        if (cls == "CDROM") return hw.Contains("MSFT") && hw.Contains("VIRTUAL") ? DeviceKind.VirtualDisk : DeviceKind.Optical;
        if (cls == "DISKDRIVE")
        {
            if (hw.Contains("MSFT") && hw.Contains("VIRTUAL")) return DeviceKind.VirtualDisk;
            if (id.StartsWith(@"SCSI\") && (hw.Contains("UASPSTOR") || hw.Contains(@"USB\VID_"))) return DeviceKind.UsbStorage;   // USB Attached SCSI: the parent is a USB device
            if (hw.Contains("SD/MMC") || hw.Contains("SDXC") || hw.Contains("SDHC") || hw.Contains("CARD READER")) return DeviceKind.MemoryCard;
            return DeviceKind.InternalDisk;
        }
        if (cls == "WPD")
            return id.StartsWith(@"SWD\WPDBUSENUM\") ? null : DeviceKind.PortableDevice;   // SWD\WPDBUSENUM entries are volumes of disks already listed
        if (id.StartsWith(@"BTHENUM\DEV_") || id.StartsWith(@"BTHLE\DEV_") || id.StartsWith(@"BTHLEDEVICE\DEV_")) return DeviceKind.Bluetooth;
        if (id.StartsWith(@"THUNDERBOLT\") || id.StartsWith(@"1394\")) return DeviceKind.Thunderbolt;
        return null;
    }

    /// <summary>USBSTOR\Disk&amp;Ven_X&amp;Prod_Y&amp;Rev_1.00\AA0011&amp;0 → "AA0011"; other ids: the last segment unless Windows generated it (second char '&amp;').</summary>
    public static string Serial(string instanceId)
    {
        var parts = instanceId.Split('\\');
        if (parts.Length < 3) return "";
        var last = parts[^1];
        if (last.Length > 1 && last[1] == '&') return "";               // generated by Windows: the device reported no serial
        var amp = last.LastIndexOf('&');
        if (amp > 0 && last[(amp + 1)..].All(char.IsDigit)) last = last[..amp];
        return MediaRegister.NormalizeSerial(last);
    }

    public static string VidPid(string text)
    {
        var m = Regex.Match(text, @"VID_([0-9A-F]{4}).*?PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return m.Success ? $"{m.Groups[1].Value.ToUpperInvariant()}:{m.Groups[2].Value.ToUpperInvariant()}" : "";
    }

    /// <summary>"Disk&amp;Ven_Kingston&amp;Prod_DataTraveler_3.0&amp;Rev_PMAP" → ("Kingston", "DataTraveler 3.0").</summary>
    public static (string Vendor, string Product) VendorProduct(string instanceId)
    {
        var m = Regex.Match(instanceId, @"VEN_([^&\\]*)&PROD_([^&\\]*)", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Value.Replace('_', ' ').Trim(), m.Groups[2].Value.Replace('_', ' ').Trim()) : ("", "");
    }

    /// <summary>Kinds the media register is about (removable and internal storage, optical, cards, portable devices).</summary>
    public static bool TrackedByRegister(DeviceKind k) => k is DeviceKind.UsbStorage or DeviceKind.MemoryCard or DeviceKind.InternalDisk or DeviceKind.Optical or DeviceKind.PortableDevice or DeviceKind.Thunderbolt;

    /// <summary>Compares a device with the media register (by serial, then by VID/PID when the device has no serial).</summary>
    public static DeviceAssessment Assess(DeviceRecord d, MediaRegister? register, DateOnly today, bool stationIsolated)
    {
        if (!TrackedByRegister(d.Kind))
        {
            var (v, w) = d.Kind switch
            {
                DeviceKind.Bluetooth => stationIsolated ? ("Dispozitiv Bluetooth asociat cu o stație izolată.", "Verificați de ce este asociat; pe o stație izolată Bluetooth trebuie să fie oprit.") : ("Dispozitiv Bluetooth asociat.", "Verificați că asocierea este cunoscută."),
                DeviceKind.NetworkDrive => stationIsolated ? ("Unitate de rețea pe o stație izolată.", "Stația izolată nu ar trebui să folosească foldere din rețea: aflați cine și când.") : ("Unitate de rețea folosită.", "Verificați că folderul este unul permis."),
                DeviceKind.VirtualDisk => ("Fișier imagine montat ca disc.", "Aflați ce fișier a fost montat și de unde provine."),
                _ => ("—", "—"),
            };
            return new(d, RegisterMatch.NotApplicable, "", v, w);
        }
        var serial = MediaRegister.NormalizeSerial(d.Serial);
        var row = register?.Rows.FirstOrDefault(r => serial.Length > 0 && MediaRegister.NormalizeSerial(r.Serial) == serial)
                  ?? (serial.Length == 0 && d.VidPid.Length > 0 ? register?.Rows.FirstOrDefault(r => r.VidPid.Trim().Equals(d.VidPid, StringComparison.OrdinalIgnoreCase)) : null);
        if (row is null)
            return d.Kind == DeviceKind.InternalDisk
                ? new(d, RegisterMatch.NotRegistered, "", "Discul intern nu este în registrul de medii.", "Adăugați discul în registru (butonul „Preia de pe stație”) sau verificați dacă a fost înlocuit.")
                : new(d, RegisterMatch.NotRegistered, "", serial.Length == 0 ? "Dispozitivul nu are serie și nu se găsește în registru." : "Dispozitivul NU este în registrul de medii.",
                      d.ConnectedNow ? "Deconectați-l și aflați al cui este; pe o stație izolată doar suporturile din registru sunt permise." : "Aflați al cui este și când a fost folosit; notați în procesul-verbal.");
        if (row.StatusValue is MediaStatus.Withdrawn or MediaStatus.Destroyed)
            return new(d, RegisterMatch.Withdrawn, row.RegistrationNumber, $"Suportul {row.RegistrationNumber} este marcat {row.Status} în registru, dar a fost văzut pe stație.", "Aflați de ce un suport retras sau distrus a fost conectat.");
        if (row.To is { } to && to < today)
            return new(d, RegisterMatch.Expired, row.RegistrationNumber, $"Suportul {row.RegistrationNumber} era valabil până la {to:yyyy-MM-dd}.", "Reînnoiți aprobarea sau retrageți suportul.");
        return new(d, RegisterMatch.Registered, row.RegistrationNumber, $"Suport înregistrat ({row.RegistrationNumber}).", "Nu este nevoie de nicio acțiune.");
    }
}

/// <summary>One line of the device detail panel: what the value is called, the value, and (when useful) what it means.</summary>
public sealed record DeviceDetail(string Label, string Value, string Meaning = "");

public static class DeviceDetails
{
    /// <summary>Every known fact about a device, in the controller's words; empty facts are said to be unknown, not hidden.</summary>
    public static IReadOnlyList<DeviceDetail> For(DeviceAssessment a)
    {
        var d = a.Device;
        string When(DateTimeOffset? t) => t is { } v ? v.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "nu este înregistrat de Windows";
        var list = new List<DeviceDetail>
        {
            new("Ce este", d.KindText, DeviceText.Explain(d.Kind)),
            new("Față de registrul de medii", a.MatchText + (a.RegistrationNumber.Length > 0 ? $" ({a.RegistrationNumber})" : ""), a.Verdict),
            new("Ce faceți", a.WhatToDo),
            new("Stare", d.ConnectedText),
            new("Nume afișat de Windows", Or(d.Name)),
            new("Producător", Or(d.Manufacturer)),
            new("Model", Or(d.Model)),
            new("Număr de serie", d.Serial.Length > 0 ? d.Serial : "fără serie proprie",
                d.Serial.Length > 0 ? "Identifică dispozitivul fizic; se compară cu registrul." : "Dispozitivul nu raportează o serie unică: Windows nu îl poate deosebi de altul identic. Verificați fizic eticheta."),
            new("VID:PID", Or(d.VidPid), d.VidPid.Length > 0 ? "Codul producătorului (VID) și al produsului (PID) pe USB." : ""),
            new("Conexiune", Or(d.Bus)),
            new("Capacitate", Or(d.CapacityText)),
            new("Literă de unitate", Or(d.DriveLetters)),
            new("Eticheta volumului", Or(d.VolumeLabel), LabelMeaning(d.VolumeLabel)),
            new("Sistem de fișiere", Or(d.FileSystem)),
            new("Criptare", Or(d.Encryption), d.Encryption.Contains("necriptat") ? "Datele de pe el pot fi citite pe orice calculator." : ""),
            new("Prima conectare", When(d.FirstConnectedUtc)),
            new("Ultima conectare", When(d.LastConnectedUtc)),
            new("Ultima deconectare", When(d.LastRemovedUtc), "Windows păstrează în fișa dispozitivului doar ULTIMA conectare; istoricul de mai jos vine din jurnal și are o durată limitată."),
            new("Istoric conectări", d.ConnectionsText.Length > 0 ? d.ConnectionsText : "nu există în jurnal"),
            new("Surse", d.Sources),
            new("Identificator Windows (tehnic)", d.InstanceId),
        };
        return list;
    }

    private static string Or(string s) => s.Length > 0 ? s : "necunoscut";

    private static string LabelMeaning(string label) =>
        MediaLabel.TryParse(label, out var p) ? $"După convenția de etichetare: cod {p.UnitCode}, număr de înregistrare {p.RegistrationNumber}, clasificare {p.Classification}." : "";
}
