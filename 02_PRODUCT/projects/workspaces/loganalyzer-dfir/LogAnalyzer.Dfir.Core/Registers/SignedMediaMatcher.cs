namespace LogAnalyzer.Dfir.Registers;

/// <summary>
/// A removable medium the station has seen (connected now or in its history), with what identifies it. <see cref="Seen"/> are the times Windows recorded
/// it (connections, last arrival); empty = unknown. Empty strings / null = attribute not known.
/// </summary>
public sealed record SeenMedium(string Serial, string VidPid, long? CapacityBytes, string BitLockerVolumeId, string Description, bool ConnectedNow,
                                    IReadOnlyList<DateTimeOffset> Seen);

/// <summary>HistoryBeforeRegister: not in the register, but seen only before the signed register existed on the station (and not connected now).</summary>
public enum SignedMediaMatchKind { Registered, NotRegistered, UsedAfterRetirement, RetiredTimeUnknown, CloneSuspected, HistoryBeforeRegister }

public sealed record SignedMediaMatch(SeenMedium Medium, SignedMediaMatchKind Kind, SignedMediaEntry? Entry, string Text)
{
    /// <summary>NECONFORM on Standalone / IsolatedNetwork.</summary>
    public bool IsViolation => Kind is SignedMediaMatchKind.NotRegistered or SignedMediaMatchKind.UsedAfterRetirement;
    /// <summary>DE VERIFICAT.</summary>
    public bool ToVerify => Kind is SignedMediaMatchKind.CloneSuspected or SignedMediaMatchKind.RetiredTimeUnknown or SignedMediaMatchKind.HistoryBeforeRegister;
}

/// <summary>
/// Recognises an observed medium in the signed register by several attributes, not by the serial alone (a serial is easy to copy onto another stick):
/// serial, VID/PID, capacity (within <see cref="CapacityTolerance"/>, Windows reports sizes from different sources) and, when both sides have it, the BitLocker
/// volume id. Same serial with a different attribute = "posibilă clonare a serialului" (DE VERIFICAT). A retired or destroyed medium seen after its
/// status date = NECONFORM.
/// </summary>
public static class SignedMediaMatcher
{
    public const double CapacityTolerance = 0.02;

    /// <param name="historyBefore">
    /// The boundary before which an unregistered medium is history (review M9): the first OFICIAL register installation from the sealed audit (or, on Standalone
    /// without it, the application's install date). Null = unknown, so every unregistered medium stays a violation.
    /// </param>
    public static SignedMediaMatch Match(IReadOnlyList<SignedMediaEntry> entries, SeenMedium m, DateTimeOffset? historyBefore = null)
    {
        var serial = MediaRegister.NormalizeSerial(m.Serial);
        SignedMediaMatch Unregistered(string text) =>
            historyBefore is { } b && !m.ConnectedNow && m.Seen.Count > 0 && m.Seen.All(s => s < b)
                ? new(m, SignedMediaMatchKind.HistoryBeforeRegister, null, $"{text} — istoric dinaintea registrului semnat (ultima dată văzut {m.Seen.Max():yyyy-MM-dd HH:mm} UTC, registrul este în vigoare din {b:yyyy-MM-dd HH:mm} UTC)")
                : new(m, SignedMediaMatchKind.NotRegistered, null, text);
        if (serial.Length == 0)
            return Unregistered($"{m.Description}: mediul nu are o serie unică, deci nu poate fi recunoscut în registrul semnat (mediu neînregistrat)");
        var same = entries.Where(e => MediaRegister.NormalizeSerial(e.UsbSerial) == serial).ToList();
        if (same.Count == 0) return Unregistered($"{m.Description}, seria {serial}: mediu neînregistrat în registrul semnat");

        var full = same.Where(e => Mismatches(e, m).Count == 0).ToList();
        if (full.Count == 0)
        {
            var e0 = same[0];
            return new(m, SignedMediaMatchKind.CloneSuspected, e0,
                $"{m.Description}, seria {serial}: posibilă clonare a serialului — seria este a mediului {e0.InventoryNumber} din registru, dar {string.Join(", ", Mismatches(e0, m))}");
        }
        // prefer the active entry (a medium re-enrolled after retirement keeps the old, retired entry too)
        var entry = full.FirstOrDefault(e => e.IsActive) ?? full[0];
        if (entry.IsActive) return new(m, SignedMediaMatchKind.Registered, entry, $"{m.Description}, seria {serial}: înregistrat ({entry.InventoryNumber}, deținător {entry.Holder})");

        var at = entry.StatusChanged;
        var after = at is { } t ? m.Seen.Where(s => s > t).OrderBy(s => s).ToList() : [];
        if (at is not null && (after.Count > 0 || m.ConnectedNow))
            return new(m, SignedMediaMatchKind.UsedAfterRetirement, entry,
                $"{m.Description}, seria {serial}: mediul {entry.InventoryNumber} este {entry.Status} din {entry.StatusChangedUtc}, dar a fost folosit după aceea" +
                (after.Count > 0 ? $" ({after[0]:yyyy-MM-dd HH:mm} UTC{(after.Count > 1 ? $" și încă {after.Count - 1}" : "")})" : "") + (m.ConnectedNow ? " și este conectat acum" : ""));
        if (m.Seen.Count == 0)
            return new(m, SignedMediaMatchKind.RetiredTimeUnknown, entry, $"{m.Description}, seria {serial}: mediul {entry.InventoryNumber} este {entry.Status} din {entry.StatusChangedUtc}; nu se știe când a fost conectat ultima dată");
        return new(m, SignedMediaMatchKind.Registered, entry, $"{m.Description}, seria {serial}: mediul {entry.InventoryNumber} ({entry.Status} din {entry.StatusChangedUtc}) a fost folosit doar înainte de această dată");
    }

    /// <summary>The attributes known on both sides that differ, in words; empty = the same medium.</summary>
    public static List<string> Mismatches(SignedMediaEntry e, SeenMedium m)
    {
        var d = new List<string>();
        var ev = RegisterRules.NormalizeVidPid(e.VidPid); var mv = RegisterRules.NormalizeVidPid(m.VidPid);
        if (ev is not null && mv is not null && ev != mv) d.Add($"VID/PID diferă (registru {ev}, observat {mv})");
        if (e.CapacityBytes > 0 && m.CapacityBytes is long mc && mc > 0 && Math.Abs(e.CapacityBytes - mc) > CapacityTolerance * Math.Max(e.CapacityBytes, mc))
            d.Add($"capacitatea diferă (registru {e.CapacityBytes} octeți, observat {mc} octeți)");
        var eb = Norm(e.BitLockerVolumeId); var mb = Norm(m.BitLockerVolumeId);
        if (eb.Length > 0 && mb.Length > 0 && eb != mb) d.Add("identificatorul volumului BitLocker diferă");
        return d;
    }

    private static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>The serials and inventory numbers of the ACTIVE media of the register (used by the transfer register: a row approves only a registered medium).</summary>
    /// <summary>
    /// As <see cref="ActiveSet(SignedMediaRegisterDoc?)"/>, plus the serials of the active media for which a medium seen on the station carries the serial but
    /// not the other registered attributes (review T1: a cloned serial must not let a transfer row approve).
    /// </summary>
    public static SignedMediaSet ActiveSet(SignedMediaRegisterDoc? doc, IEnumerable<SeenMedium>? observed)
    {
        var set = ActiveSet(doc);
        if (doc is null || observed is null) return set;
        var active = doc.Active.ToList();
        var suspect = observed.Select(m => Match(active, m)).Where(x => x.Kind == SignedMediaMatchKind.CloneSuspected)
                              .Select(x => MediaRegister.NormalizeSerial(x.Medium.Serial)).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set with { Suspect = suspect };
    }

    public static SignedMediaSet ActiveSet(SignedMediaRegisterDoc? doc) =>
        new(doc?.Active.Select(e => MediaRegister.NormalizeSerial(e.UsbSerial)).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [],
            doc?.Active.Select(e => e.InventoryNumber.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [])
        { SerialByInventory = doc?.Active.Where(e => e.InventoryNumber.Trim().Length > 0).GroupBy(e => e.InventoryNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                                  .ToDictionary(g => g.Key, g => MediaRegister.NormalizeSerial(g.First().UsbSerial), StringComparer.OrdinalIgnoreCase) ?? [] };
}

/// <summary>The active media of the signed register, by serial and inventory number. Empty = no register in force (nothing is registered).</summary>
public sealed record SignedMediaSet(IReadOnlySet<string> Serials, IReadOnlySet<string> InventoryNumbers)
{
    /// <summary>Registered serials seen on the station with other attributes (posibilă clonare).</summary>
    public IReadOnlySet<string> Suspect { get; init; } = new HashSet<string>();
    public IReadOnlyDictionary<string, string> SerialByInventory { get; init; } = new Dictionary<string, string>();

    public bool Contains(string serial, string inventoryNumber)
    {
        var s = MediaRegister.NormalizeSerial(serial);
        if (s.Length > 0) return Serials.Contains(s);
        return inventoryNumber.Trim().Length > 0 && InventoryNumbers.Contains(inventoryNumber.Trim());
    }

    /// <summary>True when the medium (by serial, or by the serial of its inventory number) was seen with attributes that differ from its register entry.</summary>
    public bool IsSuspect(string serial, string inventoryNumber)
    {
        var s = MediaRegister.NormalizeSerial(serial);
        if (s.Length == 0 && SerialByInventory.TryGetValue(inventoryNumber.Trim(), out var bySerial)) s = bySerial;
        return s.Length > 0 && Suspect.Contains(s);
    }
}
