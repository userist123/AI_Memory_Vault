extern alias ezregistry;

using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using ezregistry::Registry;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Deleted keys and values of device history recovered from an offline hive (D18): Enum\USBSTOR, Enum\USB, MountedDevices (SYSTEM), Windows Portable
/// Devices and EMDMgmt (SOFTWARE), MountPoints2 (NTUSER). Uses the Registry library (Eric Zimmerman, MIT) in recover-deleted mode, which reads the
/// free cells of the hive that our own <c>RawRegistry</c> reader does not look at. The hive is only read; the recovered record is whatever bytes
/// survived in free space, so every item is an observation of a past record, not proof of who deleted it or why.
/// </summary>
public static class DeletedRegistryTraces
{
    public const string EventSource = "DeletedRegistryKey";

    /// <summary>One recovered deleted key or value under a watched area.</summary>
    public sealed record Entry(string Hive, string Area, string KeyPath, string ValueName, DateTimeOffset? LastWriteUtc, string Serial, string VolumeGuid);

    private sealed record Watch(string Hive, string Area, Regex Path);

    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Watch[] Watches =
    [
        new("SYSTEM", "Enum\\USBSTOR", Rx(@"^ControlSet\d+\\Enum\\USBSTOR(\\|$)")),
        new("SYSTEM", "Enum\\USB", Rx(@"^ControlSet\d+\\Enum\\USB(\\|$)")),
        new("SYSTEM", "MountedDevices", Rx(@"^MountedDevices(\\|$)")),
        new("SOFTWARE", "Windows Portable Devices", Rx(@"^Microsoft\\Windows Portable Devices(\\|$)")),
        new("SOFTWARE", "EMDMgmt", Rx(@"^Microsoft\\Windows NT\\CurrentVersion\\EMDMgmt(\\|$)")),
        new("NTUSER", "MountPoints2", Rx(@"^Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\MountPoints2(\\|$)")),
    ];

    private static readonly Regex SerialRx = Rx(@"USBSTOR[#\\][^#\\]+[#\\](?<s>[^#\\]+)");
    private static readonly Regex VolumeRx = Rx(@"Volume(?<g>\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\})|^(?<g>\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\})(?=\s|$)");

    /// <summary>
    /// Reads the hive and returns the deleted keys and values that sit under the areas watched for <paramref name="hive"/>. Throws when the library cannot
    /// read the file: the caller reports that as a gap, never as "nothing was deleted".
    /// </summary>
    public static IReadOnlyList<Entry> Read(string hivePath, string hive)
    {
        var watches = Watches.Where(w => w.Hive.Equals(hive, StringComparison.OrdinalIgnoreCase)).ToList();
        var result = new List<Entry>();
        // The library takes the whole file into memory; the file is opened read-only and shared with writers.
        byte[] bytes;
        using (var fs = new FileStream(hivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
        }
        var h = new RegistryHive(bytes, hivePath) { RecoverDeleted = true };
        if (!h.ParseHive()) throw new InvalidDataException("biblioteca Registry nu a putut interpreta hive-ul");

        // A deleted key comes with the deleted subkeys that hung under it, so the tree is walked; a key can also be listed on its own.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(ezregistry::Registry.Abstractions.RegistryKey k, int depth)
        {
            var path = Normalize(k.KeyPath);
            if (watches.FirstOrDefault(w => w.Path.IsMatch(path)) is { } w && seen.Add(path + "|"))
            {
                var leaf = path[(path.LastIndexOf('\\') + 1)..];
                result.Add(Make(w, path, "", k.LastWriteTime, leaf + " " + path));
                foreach (var v in k.Values)
                    if (seen.Add(path + "|" + v.ValueName))
                        result.Add(Make(w, path, v.ValueName, k.LastWriteTime, v.ValueName + " " + leaf));
            }
            if (depth < 64)
                foreach (var child in k.SubKeys) Walk(child, depth + 1);
        }
        foreach (var k in h.DeletedRegistryKeys) Walk(k, 0);
        // Deleted values whose key could not be matched: only the value name tells where they belonged. MountedDevices values are the recognisable ones.
        foreach (var v in h.UnassociatedRegistryValues)
        {
            var name = v.ValueName ?? "";
            if (watches.FirstOrDefault(w => w.Area == "MountedDevices") is not { } w) continue;
            if (!(name.StartsWith(@"\DosDevices\", StringComparison.OrdinalIgnoreCase) || name.StartsWith(@"\??\Volume", StringComparison.OrdinalIgnoreCase))) continue;
            var text = v.ValueDataRaw is { Length: > 8 } raw ? System.Text.Encoding.Unicode.GetString(raw) : "";
            result.Add(Make(w, "MountedDevices", name, null, name + " " + text));
        }
        return result.OrderBy(e => e.KeyPath, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.ValueName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Entry Make(Watch w, string path, string valueName, DateTimeOffset? lastWrite, string text)
    {
        var s = SerialRx.Match(text);
        var serial = s.Success ? s.Groups["s"].Value : w.Area is "Enum\\USBSTOR" or "Enum\\USB" ? SerialFromEnumPath(path) : "";
        var vol = VolumeRx.Match(text);
        return new(w.Hive, w.Area, path, valueName, lastWrite, StripInstanceSuffix(serial), vol.Success ? vol.Groups["g"].Value.ToUpperInvariant() : "");
    }

    /// <summary>"ControlSet001\Enum\USBSTOR\Disk&amp;Ven_X\AA11&amp;0" -> "AA11&amp;0"; a class key (no instance) has no serial.</summary>
    private static string SerialFromEnumPath(string path)
    {
        var parts = path.Split('\\');
        return parts.Length >= 5 ? parts[4] : "";
    }

    /// <summary>The instance suffix Windows appends ("AA11&amp;0") is not part of the serial.</summary>
    private static string StripInstanceSuffix(string s) => s.Length > 2 && s[^2] == '&' && char.IsDigit(s[^1]) ? s[..^2] : s;

    /// <summary>Drops the hive's root key name (it differs between tools) and the control-set alias.</summary>
    private static string Normalize(string keyPath)
    {
        var i = keyPath.IndexOf('\\');
        return i < 0 ? "" : keyPath[(i + 1)..];
    }

    /// <summary>Emits one timeline event per recovered entry. A hive the library cannot read becomes a gap, not an empty result.</summary>
    internal static void Emit(string fullPath, string hive, EvidenceItem item, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        IReadOnlyList<Entry> entries;
        try { entries = Read(fullPath, hive); }
        // The library parses untrusted bytes and signals damage with many exception types (key lookups, bounds, overflow). Any failure is reported as a gap below.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Gaps.Add(new EvidenceGap("Chei șterse din registry", EvidenceStatus.Partial,
                $"recuperarea cheilor șterse din hive-ul {hive} a eșuat: {ex.Message}",
                "Nu se poate spune dacă urmele de dispozitive au fost șterse; lipsa rezultatului nu înseamnă că nimic nu a fost șters",
                "Hive-ul cu jurnalele de tranzacții (.LOG1/.LOG2), copie a hive-ului luată cu o altă unealtă", "Parțial"));
            return;
        }
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            var what = e.ValueName.Length > 0 ? $"valoarea „{e.ValueName}” a cheii ștearse" : "cheia ștearsă";
            var when = e.LastWriteUtc;
            sink.Add(new TimelineEvent
            {
                Time = when is { } t ? Timestamp.FromUtc(t.UtcDateTime, t.ToString("o", CultureInfo.InvariantCulture), "deleted registry key last write (before deletion)") : Timestamp.Unknown(),
                TimeSemantics = "last write of the key before it was deleted (not the time of deletion, which the hive does not keep)",
                Source = EventSource, EvidenceId = item.EvidenceId, Path = "", Process = "",
                Summary = $"Registry ({e.Hive}): {what} din {e.Area}: {e.KeyPath}",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $@"{e.Hive}\{e.KeyPath}#deleted{(e.ValueName.Length > 0 ? "\\" + e.ValueName : "")}",
                Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Hive"] = e.Hive, ["Area"] = e.Area, ["KeyPath"] = e.KeyPath, ["ValueName"] = e.ValueName,
                    ["Serial"] = e.Serial, ["VolumeGuid"] = e.VolumeGuid, ["Deleted"] = e.ValueName.Length > 0 ? "value" : "key",
                    ["LastWriteUtc"] = when?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture) ?? "",
                },
            });
            result.Records++;
        }
    }
}
