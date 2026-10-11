using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Devices;

/// <summary>One protection Windows itself can apply to devices, read from the station and said in plain words.</summary>
public sealed record DeviceProtection(string Title, string State, bool Protected, string Meaning, string Source, bool Known = true)
{
    public string ProtectedText => !Known ? "necunoscut" : Protected ? "da" : "NU";
}

/// <summary>
/// The native Windows protections against unapproved media (research §4.5): Removable Storage Access policy (whole and per class), Device
/// Installation Restrictions, BitLocker To Go write protection, AutoRun, USB storage driver start, and whether copying to removable media is
/// audited at all. Read-only. A value that cannot be read is reported as unknown, never as protected.
/// </summary>
public static class DevicePolicyCheck
{
    private const string RS = @"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices";

    private static readonly (string Guid, string Name)[] StorageClasses =
    [
        ("{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}", "discuri amovibile (stick-uri, discuri externe)"),
        ("{53f56308-b6bf-11d0-94f2-00a0c91efb8b}", "CD/DVD"),
        ("{6AC27878-A6FA-4155-BA85-F98F491D4F33}", "telefoane și dispozitive portabile (WPD)"),
        ("{53f56311-b6bf-11d0-94f2-00a0c91efb8b}", "dischete"),
        ("{53f5630b-b6bf-11d0-94f2-00a0c91efb8b}", "benzi magnetice"),
    ];

    public static IReadOnlyList<DeviceProtection> Read(Func<string, string, object?>? hklm = null, Func<IReadOnlyDictionary<string, string>?>? audit = null)
    {
        hklm ??= (key, name) => { try { return Registry.LocalMachine.OpenSubKey(key)?.GetValue(name); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return "?"; } };
        string V(string key, string name) => Convert.ToString(hklm(key, name)) ?? "";
        var r = new List<DeviceProtection>();

        var denyAll = V(RS, "Deny_All") == "1";
        r.Add(new("Acces la orice stocare amovibilă", denyAll ? "interzis prin politică" : "nu este interzis global", denyAll,
            "Politica „Toate clasele de stocare amovibilă: interzice orice acces” blochează stick-urile, discurile externe, CD/DVD și telefoanele ca stocare.",
            @"HKLM\" + RS + @" → Deny_All"));
        foreach (var (g, name) in StorageClasses)
        {
            var read = V($@"{RS}\{g}", "Deny_Read") == "1"; var write = V($@"{RS}\{g}", "Deny_Write") == "1"; var exec = V($@"{RS}\{g}", "Deny_Execute") == "1";
            var state = denyAll ? "interzis (prin regula globală)" : read && write ? "citire și scriere interzise" : write ? "scriere interzisă" : read ? "citire interzisă" : "permis";
            r.Add(new($"Acces la {name}", state + (exec ? "; rularea programelor interzisă" : ""), denyAll || read || write,
                write || denyAll ? "Datele nu pot fi scrise pe aceste suporturi, deci nu pot fi scoase de pe stație pe ele." : "Windows nu împiedică scrierea pe aceste suporturi; doar procedura și registrul de medii o controlează.",
                $@"HKLM\{RS}\{g} → Deny_Read / Deny_Write / Deny_Execute"));
        }

        var usbStart = V(@"SYSTEM\CurrentControlSet\Services\USBSTOR", "Start");
        r.Add(new("Componenta Windows pentru stick-uri USB (USBSTOR)", usbStart switch { "4" => "dezactivată", "3" => "activă (implicit)", "" => "necunoscut", _ => "valoare " + usbStart }, usbStart == "4",
            "Dacă este dezactivată, stick-urile și discurile USB nu mai funcționează pe stație (telefoanele MTP și cardurile din cititoare interne nu sunt afectate).",
            @"HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR → Start"));

        const string DI = @"SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions";
        var denyRemovable = V(DI, "DenyRemovableDevices") == "1"; var denyUnspecified = V(DI, "DenyUnspecified") == "1";
        var denyIds = V(DI, "DenyDeviceIDs") == "1"; var denyClasses = V(DI, "DenyDeviceClasses") == "1"; var allowIds = V(DI, "AllowDeviceIDs") == "1";
        var any = denyRemovable || denyUnspecified || denyIds || denyClasses;
        r.Add(new("Restricții la instalarea dispozitivelor noi",
            any ? string.Join("; ", new[] { denyRemovable ? "dispozitive amovibile interzise" : null, denyUnspecified ? "orice dispozitiv neaprobat interzis" : null,
                                            denyIds ? "anumite dispozitive interzise" : null, denyClasses ? "anumite clase interzise" : null, allowIds ? "listă de dispozitive aprobate" : null }.Where(s => s is not null))
                : "nicio restricție", any,
            "Windows refuză să instaleze dispozitivele interzise; un stick nou nu mai poate fi folosit. Dispozitivele deja instalate rămân utilizabile dacă regula nu este „retroactivă”.",
            @"HKLM\" + DI));

        var rdv = V(@"SYSTEM\CurrentControlSet\Policies\Microsoft\FVE", "RDVDenyWriteAccess") is { Length: > 0 } a ? a : V(@"SOFTWARE\Policies\Microsoft\FVE", "RDVDenyWriteAccess");
        r.Add(new("Scriere doar pe stick-uri criptate (BitLocker To Go)", rdv == "1" ? "impusă" : "neimpusă", rdv == "1",
            "Dacă este impusă, pe un stick necriptat se poate doar citi; datele scoase sunt astfel mereu criptate.",
            @"HKLM\SYSTEM\CurrentControlSet\Policies\Microsoft\FVE (sau SOFTWARE\Policies\Microsoft\FVE) → RDVDenyWriteAccess"));

        var autorun = V(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun");
        var autorunOff = int.TryParse(autorun, out var ar) && (ar & 0x04) != 0 && (ar & 0x20) != 0;
        r.Add(new("Pornirea automată a programelor de pe suporturi (AutoRun)", autorun.Length == 0 ? "setare implicită Windows" : autorunOff ? "oprită pentru stick-uri și CD" : $"valoare {autorun}", autorunOff,
            "Cu AutoRun pornit, un program de pe un stick sau CD poate porni singur la introducere (cale veche de infectare).",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer → NoDriveTypeAutoRun"));

        var a2 = audit?.Invoke();
        if (audit is not null && a2 is null)
        {
            r.Add(new("Înregistrarea copierii de fișiere pe suporturi", "necunoscut", false, "Politica de audit se citește doar cu drepturi de administrator: porniți aplicația ca administrator ca să aflați dacă copierea pe stick-uri se înregistrează.", "auditpol → Removable Storage", Known: false));
            r.Add(new("Înregistrarea conectării dispozitivelor", "necunoscut", false, "Politica de audit se citește doar cu drepturi de administrator.", "auditpol → Plug and Play Events", Known: false));
        }
        if (a2 is not null)
        {
            var rem = a2.GetValueOrDefault("Removable Storage", "");
            var pnp = a2.GetValueOrDefault("Plug and Play Events", "");
            r.Add(new("Înregistrarea copierii de fișiere pe suporturi", rem.Contains("Success", StringComparison.OrdinalIgnoreCase) ? "activă" : "INACTIVĂ", rem.Contains("Success", StringComparison.OrdinalIgnoreCase),
                rem.Contains("Success", StringComparison.OrdinalIgnoreCase) ? "Fiecare fișier citit sau scris pe un suport amovibil lasă un eveniment (Security 4663)." : "Ce s-a copiat pe stick-uri NU se înregistrează pe această stație; controlul nu poate spune ce fișiere au ieșit.",
                "auditpol → Removable Storage"));
            r.Add(new("Înregistrarea conectării dispozitivelor", pnp.Contains("Success", StringComparison.OrdinalIgnoreCase) ? "activă" : "INACTIVĂ", pnp.Contains("Success", StringComparison.OrdinalIgnoreCase),
                "Cu ea activă, fiecare dispozitiv extern recunoscut lasă un eveniment (Security 6416) cu ora și utilizatorul.",
                "auditpol → Plug and Play Events"));
        }
        return r;
    }
}
