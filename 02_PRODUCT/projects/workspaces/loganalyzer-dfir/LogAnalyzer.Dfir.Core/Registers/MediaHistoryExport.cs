using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Devices;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>One connection of a medium on a station: when it was connected and, when Windows recorded it, when it was removed ("" = not known / still connected).</summary>
public sealed class MediumConnectionWindow
{
    public string StartUtc { get; set; } = "";
    public string EndUtc { get; set; } = "";
    public DateTimeOffset? Start => SignedMediaRegister.TryUtc(StartUtc);
    public DateTimeOffset? End => SignedMediaRegister.TryUtc(EndUtc);
}

/// <summary>
/// A file the station saw going to a medium or coming from it (A15 / classified access: write on a removable medium, copy or archive command naming it,
/// shortcut or jump list opened from it). <see cref="Direction"/> is <see cref="MediaHistoryExport.ToMedium"/> or <see cref="MediaHistoryExport.FromMedium"/>.
/// </summary>
public sealed class MediumFileActivity
{
    public string File { get; set; } = "";
    /// <summary>Lower-case hex SHA-256 of the file when the station knows it; "" = not known.</summary>
    public string Sha256 { get; set; } = "";
    public string TimeUtc { get; set; } = "";
    public string Direction { get; set; } = "";
    /// <summary>Where the station saw it ("A15: scriere pe suport amovibil", …).</summary>
    public string Source { get; set; } = "";
    /// <summary>The station's own verdict on the exit (IEȘIRE NEJUSTIFICATĂ / DE VERIFICAT / APROBATĂ).</summary>
    public string Verdict { get; set; } = "";
    public DateTimeOffset? Time => SignedMediaRegister.TryUtc(TimeUtc);
}

/// <summary>One removable medium a station knows (connected now or in its history), with every connection and file activity it recorded.</summary>
public sealed class MediumHistory
{
    public string Serial { get; set; } = "";
    public string VidPid { get; set; } = "";
    public long CapacityBytes { get; set; }
    public string BitLockerVolumeId { get; set; } = "";
    public List<string> VolumeSerials { get; set; } = [];
    public string Description { get; set; } = "";
    public string DriveLetters { get; set; } = "";
    public bool ConnectedNow { get; set; }
    public string FirstConnectedUtc { get; set; } = "";
    public string LastConnectedUtc { get; set; } = "";
    public List<MediumConnectionWindow> Connections { get; set; } = [];
    public List<MediumFileActivity> Files { get; set; } = [];

    public string NormalizedSerial => MediaRegister.NormalizeSerial(Serial);
}

/// <summary>A row of the station's transfer register that involves a medium (serial or medium register number).</summary>
public sealed class MediaTransferEntry
{
    public string RegistrationNumber { get; set; } = "";
    public string DateTime { get; set; } = "";
    public string Direction { get; set; } = "";
    public string SourceStation { get; set; } = "";
    public string DestinationStation { get; set; } = "";
    public string MediumSerial { get; set; } = "";
    public string MediumRegistration { get; set; } = "";
    public string Content { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Person { get; set; } = "";
    public string Approver { get; set; } = "";

    public static MediaTransferEntry From(TransferRow r) => new()
    {
        RegistrationNumber = r.RegistrationNumber.Trim(), DateTime = r.DateTime.Trim(), Direction = r.Direction.Trim(), SourceStation = r.SourceStation.Trim(),
        DestinationStation = r.DestinationStation.Trim(), MediumSerial = r.MediumSerial.Trim(), MediumRegistration = r.MediumRegistration.Trim(), Content = r.Content.Trim(),
        Sha256 = r.Sha256.Trim(), Person = r.Person.Trim(), Approver = r.Approver.Trim(),
    };
}

/// <summary>
/// A tamper indicator of the station at export time, from the control just run (review R5): A05 (event logs cleared, 104/1102), A11 (anchor continuity),
/// A12 (event log service stopped), A13 (officer list), A16 (application install date), S05 (clock against the application's time memory). <see cref="Status"/>
/// is the control's word (CONFORM, NECONFORM, DE VERIFICAT, NEDETERMINAT, NU SE APLICĂ) or "lipsă" when the check did not run.
/// </summary>
public sealed class StationIndicator
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>CONFORM, or NU SE APLICĂ (the check does not apply to the edition): nothing that hides history.</summary>
    public bool Clean => Status is MediaHistoryExport.StatusConform or MediaHistoryExport.StatusNotApplicable;
}

/// <summary>A classified file present on the station at export time, with its SHA-256 (from the control's hashing).</summary>
public sealed class PresentFile
{
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// A station's history of removable media for the security officer (owner decision 2026-10-16): every station with the application exports it; the officer,
/// physically present, signs it with the qualified card certificate (detached CMS, ".p7s") when collecting it and reconciles the exports of all stations on
/// the officer's station. The canonical form follows the rules of <see cref="SignedMediaRegister"/>: keys sorted (ordinal), no whitespace, UTF-8 without BOM.
/// </summary>
public sealed class MediaHistoryDoc
{
    public string Format { get; set; } = MediaHistoryExport.FormatId;
    /// <summary>The station's Hardware ID (the licence identity).</summary>
    public string StationId { get; set; } = "";
    /// <summary>The station's computer name (the name the transfer register uses).</summary>
    public string StationName { get; set; } = "";
    public string Edition { get; set; } = "";
    public string Posture { get; set; } = "";
    /// <summary>Windows time zone id of the station: the transfer register times are typed in it.</summary>
    public string TimeZone { get; set; } = "";
    public string ExportedUtc { get; set; } = "";
    /// <summary>The anchor of the application's sealed audit at export ("" / 0 = not available).</summary>
    public string AnchorCode { get; set; } = "";
    public long AnchorSeq { get; set; }
    /// <summary>The officer-signed media register installed on the station (0 = none in force) and its SHA-256.</summary>
    public long RegisterVersion { get; set; }
    public string RegisterSha256 { get; set; } = "";
    /// <summary>"da" / "nu" / "necunoscut": whether the station audits access to files (A14); without it the file activity is not visible.</summary>
    public string FileAuditing { get; set; } = "necunoscut";
    /// <summary>The oldest time the station's media history reaches ("" = not known).</summary>
    public string HistoryFromUtc { get; set; } = "";
    public List<MediumHistory> Media { get; set; } = [];
    /// <summary>File activity on a removable medium the station could not attribute to one medium (no serial in the record).</summary>
    public List<MediumFileActivity> UnattributedFiles { get; set; } = [];
    public List<MediaTransferEntry> Transfers { get; set; } = [];
    public List<PresentFile> PresentFiles { get; set; } = [];
    /// <summary>The station's tamper indicators at export time (A05, A11, A12, A13, A16, S05).</summary>
    public List<StationIndicator> Indicators { get; set; } = [];
    public List<string> Notes { get; set; } = [];

    public DateTimeOffset? Exported => SignedMediaRegister.TryUtc(ExportedUtc);
    public DateTimeOffset? HistoryFrom => SignedMediaRegister.TryUtc(HistoryFromUtc);
    public string Label => StationName.Length > 0 ? StationName : StationId;
}

/// <summary>What the station knows when the export is built (all of it read by the caller; the build itself is pure).</summary>
public sealed record MediaHistoryInput
{
    public required string StationId { get; init; }
    public required string StationName { get; init; }
    public string Edition { get; init; } = "";
    public string Posture { get; init; } = "";
    public string TimeZone { get; init; } = "";
    public required DateTimeOffset NowUtc { get; init; }
    public LogAnalyzer.Dfir.Case.AuditAnchorPoint? Anchor { get; init; }
    public ActiveMediaRegister? Register { get; init; }
    public IReadOnlyList<DeviceRecord> Devices { get; init; } = [];
    public ClassifiedAccessReport? Classified { get; init; }
    public IReadOnlyDictionary<string, string>? FileHashes { get; init; }
    public TransferRegister? Transfers { get; init; }
    public string FileAuditing { get; init; } = "necunoscut";
    public IReadOnlyList<StationIndicator> Indicators { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public static class MediaHistoryExport
{
    public const string FormatId = "loganalyzer.media_history.v1";
    public const string ToMedium = "catre_mediu", FromMedium = "de_pe_mediu";
    public const string SignatureExtension = ".p7s";
    public const string StatusConform = "CONFORM", StatusNotApplicable = "NU SE APLICĂ", StatusMissing = "lipsă";
    /// <summary>The control checks whose state shows that a station's history may have been cleared or its clock moved (review R5, R4).</summary>
    public static readonly string[] IndicatorIds = ["A05", "A11", "A12", "A13", "A16", "S05"];

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string FileName(MediaHistoryDoc d)
    {
        var name = new string((d.Label.Length > 0 ? d.Label : "statie").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return $"istoric_medii_{name}_{(d.Exported ?? DateTimeOffset.UnixEpoch).ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.json";
    }

    // ---- build ----------------------------------------------------------------------------------------------------------------------------

    private static readonly Regex SerialInTarget = new(@"\(SN ([^)\s]+)\)", RegexOptions.Compiled);
    private static readonly Regex VolumeInTarget = new(@"serie volum ([0-9A-Fa-f]{4}-?[0-9A-Fa-f]{4})", RegexOptions.Compiled);

    /// <summary>The export of one station. Pure: everything comes from <paramref name="i"/>.</summary>
    public static MediaHistoryDoc Build(MediaHistoryInput i)
    {
        var doc = new MediaHistoryDoc
        {
            StationId = i.StationId.Trim(), StationName = i.StationName.Trim(), Edition = i.Edition, Posture = i.Posture, TimeZone = i.TimeZone,
            ExportedUtc = SignedMediaRegister.FormatUtc(i.NowUtc), AnchorCode = i.Anchor?.Code ?? "", AnchorSeq = i.Anchor?.Seq ?? 0,
            RegisterVersion = i.Register is { Usable: true } r ? r.Doc!.Version : 0, RegisterSha256 = i.Register is { Usable: true } r2 ? r2.Sha256 : "",
            FileAuditing = i.FileAuditing is "da" or "nu" ? i.FileAuditing : "necunoscut",
        };
        if (i.Register is { Usable: false } bad) doc.Notes.Add("registrul semnat de medii nu este în vigoare pe stație: " + bad.Problem);
        if (i.Anchor is null) doc.Notes.Add("codul de ancorare al jurnalului aplicației nu a fost disponibil la export");

        foreach (var d in i.Devices.Where(SignedMediaEnrollment.IsRemovableMedium))
        {
            var starts = d.Connections.Concat(d.LastConnectedUtc is { } l ? new[] { l } : Array.Empty<DateTimeOffset>()).Concat(d.FirstConnectedUtc is { } f ? new[] { f } : Array.Empty<DateTimeOffset>())
                          .Select(t => t.ToUniversalTime()).Select(t => new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, TimeSpan.Zero))
                          .Distinct().OrderBy(t => t).ToList();
            var m = new MediumHistory
            {
                Serial = MediaRegister.NormalizeSerial(d.Serial), VidPid = (d.VidPid ?? "").ToLowerInvariant(), CapacityBytes = d.CapacityBytes ?? 0, BitLockerVolumeId = d.BitLockerVolumeId,
                VolumeSerials = [.. d.VolumeSerials.Select(v => v.Trim().ToUpperInvariant()).Where(v => v.Length > 0).Distinct()],
                Description = $"{d.Manufacturer} {(d.Model.Length > 0 ? d.Model : d.Name)}".Trim(), DriveLetters = d.DriveLetters, ConnectedNow = d.ConnectedNow,
                FirstConnectedUtc = starts.Count > 0 ? SignedMediaRegister.FormatUtc(starts[0]) : "", LastConnectedUtc = starts.Count > 0 ? SignedMediaRegister.FormatUtc(starts[^1]) : "",
            };
            for (int k = 0; k < starts.Count; k++)
            {
                // Windows logs every arrival (1006) but keeps only the LAST removal: the end is known only for the last window
                var end = k == starts.Count - 1 && !d.ConnectedNow && d.LastRemovedUtc is { } rem && rem.ToUniversalTime() > starts[k] ? SignedMediaRegister.FormatUtc(rem) : "";
                m.Connections.Add(new MediumConnectionWindow { StartUtc = SignedMediaRegister.FormatUtc(starts[k]), EndUtc = end });
            }
            doc.Media.Add(m);
        }

        if (i.Classified is { } c)
            foreach (var x in c.Exits.Where(x => x.Channel is ExitChannel.RemovableMedia or ExitChannel.CopyCommand or ExitChannel.Archive or ExitChannel.OpenedFromMedium))
            {
                var act = new MediumFileActivity
                {
                    File = Path.GetFileName(x.File), Sha256 = (i.FileHashes?.GetValueOrDefault(x.File) ?? "").ToLowerInvariant(),
                    TimeUtc = x.TimeUtc is { } t ? SignedMediaRegister.FormatUtc(t) : "", Direction = x.Channel == ExitChannel.OpenedFromMedium ? FromMedium : ToMedium,
                    Source = "A15: " + x.ChannelText, Verdict = x.VerdictText,
                };
                var target = doc.Media.FirstOrDefault(m => SerialInTarget.Match(x.Target) is { Success: true } sm && m.Serial.Length > 0 && m.Serial == MediaRegister.NormalizeSerial(sm.Groups[1].Value))
                          ?? doc.Media.FirstOrDefault(m => VolumeInTarget.Match(x.Target) is { Success: true } vm && m.VolumeSerials.Contains(Volume(vm.Groups[1].Value)));
                (target?.Files ?? doc.UnattributedFiles).Add(act);
            }
        foreach (var m in doc.Media) m.Files.Sort((a, b) => string.CompareOrdinal(a.TimeUtc, b.TimeUtc));

        foreach (var row in i.Transfers?.Rows ?? [])
            if (row.MediumSerial.Trim().Length > 0 || row.MediumRegistration.Trim().Length > 0) doc.Transfers.Add(MediaTransferEntry.From(row));

        foreach (var (path, sha) in (i.FileHashes ?? new Dictionary<string, string>()).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            if (sha.Length == 64) doc.PresentFiles.Add(new PresentFile { Path = path, Sha256 = sha.ToLowerInvariant() });

        var oldest = doc.Media.SelectMany(m => m.Connections).Select(w => w.Start).Where(t => t is not null).Min();
        doc.HistoryFromUtc = oldest is { } o ? SignedMediaRegister.FormatUtc(o) : "";
        foreach (var id in IndicatorIds)
            doc.Indicators.Add(i.Indicators.FirstOrDefault(x => x.Id == id) is { } ind
                ? new StationIndicator { Id = ind.Id, Title = ind.Title, Status = ind.Status, Detail = ind.Detail }
                : new StationIndicator { Id = id, Status = StatusMissing, Detail = "verificarea nu a rulat la acest control" });
        doc.Notes.AddRange(i.Notes);
        return doc;
    }

    private static string Volume(string v) { var s = v.Replace("-", "").ToUpperInvariant(); return s.Length == 8 ? s[..4] + "-" + s[4..] : s; }

    // ---- canonical form -------------------------------------------------------------------------------------------------------------------

    private static SortedDictionary<string, object> Map(MediaHistoryDoc d) => new(StringComparer.Ordinal)
    {
        ["anchor_code"] = d.AnchorCode ?? "", ["anchor_seq"] = d.AnchorSeq, ["edition"] = d.Edition ?? "", ["exported_utc"] = d.ExportedUtc ?? "", ["file_auditing"] = d.FileAuditing ?? "",
        ["format"] = d.Format ?? "", ["history_from_utc"] = d.HistoryFromUtc ?? "", ["indicators"] = d.Indicators.Select(Map).Cast<object>().ToList(), ["media"] = d.Media.Select(Map).Cast<object>().ToList(),
        ["notes"] = d.Notes.Cast<object>().ToList(), ["posture"] = d.Posture ?? "", ["present_files"] = d.PresentFiles.Select(Map).Cast<object>().ToList(),
        ["register_sha256"] = d.RegisterSha256 ?? "", ["register_version"] = d.RegisterVersion, ["station_id"] = d.StationId ?? "", ["station_name"] = d.StationName ?? "",
        ["time_zone"] = d.TimeZone ?? "", ["transfers"] = d.Transfers.Select(Map).Cast<object>().ToList(), ["unattributed_files"] = d.UnattributedFiles.Select(Map).Cast<object>().ToList(),
    };

    private static SortedDictionary<string, object> Map(MediumHistory m) => new(StringComparer.Ordinal)
    {
        ["bitlocker_volume_id"] = m.BitLockerVolumeId ?? "", ["capacity_bytes"] = m.CapacityBytes, ["connected_now"] = m.ConnectedNow,
        ["connections"] = m.Connections.Select(w => (object)new SortedDictionary<string, object>(StringComparer.Ordinal) { ["end_utc"] = w.EndUtc ?? "", ["start_utc"] = w.StartUtc ?? "" }).ToList(),
        ["description"] = m.Description ?? "", ["drive_letters"] = m.DriveLetters ?? "", ["files"] = m.Files.Select(Map).Cast<object>().ToList(), ["first_connected_utc"] = m.FirstConnectedUtc ?? "",
        ["last_connected_utc"] = m.LastConnectedUtc ?? "", ["serial"] = m.Serial ?? "", ["vid_pid"] = m.VidPid ?? "", ["volume_serials"] = m.VolumeSerials.Cast<object>().ToList(),
    };

    private static SortedDictionary<string, object> Map(MediumFileActivity f) => new(StringComparer.Ordinal)
    {
        ["direction"] = f.Direction ?? "", ["file"] = f.File ?? "", ["sha256"] = f.Sha256 ?? "", ["source"] = f.Source ?? "", ["time_utc"] = f.TimeUtc ?? "", ["verdict"] = f.Verdict ?? "",
    };

    private static SortedDictionary<string, object> Map(MediaTransferEntry t) => new(StringComparer.Ordinal)
    {
        ["approver"] = t.Approver ?? "", ["content"] = t.Content ?? "", ["date_time"] = t.DateTime ?? "", ["destination_station"] = t.DestinationStation ?? "", ["direction"] = t.Direction ?? "",
        ["medium_registration"] = t.MediumRegistration ?? "", ["medium_serial"] = t.MediumSerial ?? "", ["person"] = t.Person ?? "", ["registration_number"] = t.RegistrationNumber ?? "",
        ["sha256"] = t.Sha256 ?? "", ["source_station"] = t.SourceStation ?? "",
    };

    private static SortedDictionary<string, object> Map(StationIndicator x) => new(StringComparer.Ordinal)
    {
        ["detail"] = x.Detail ?? "", ["id"] = x.Id ?? "", ["status"] = x.Status ?? "", ["title"] = x.Title ?? "",
    };

    private static SortedDictionary<string, object> Map(PresentFile p) => new(StringComparer.Ordinal) { ["path"] = p.Path ?? "", ["sha256"] = p.Sha256 ?? "" };

    /// <summary>The canonical bytes the officer signs: keys sorted (ordinal), no insignificant whitespace, UTF-8 without BOM; lists keep their order.</summary>
    public static byte[] Canonical(MediaHistoryDoc doc)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, WriterOptions)) Write(w, Map(doc));
        return ms.ToArray();
    }

    private static void Write(Utf8JsonWriter w, object v)
    {
        switch (v)
        {
            case string s: w.WriteStringValue(s); break;
            case long n: w.WriteNumberValue(n); break;
            case bool b: w.WriteBooleanValue(b); break;
            case SortedDictionary<string, object> o:
                w.WriteStartObject();
                foreach (var (k, x) in o) { w.WritePropertyName(k); Write(w, x); }
                w.WriteEndObject();
                break;
            case List<object> a:
                w.WriteStartArray();
                foreach (var x in a) Write(w, x);
                w.WriteEndArray();
                break;
            default: throw new InvalidOperationException("tip neașteptat în forma canonică: " + v.GetType().Name);
        }
    }

    // ---- parse ----------------------------------------------------------------------------------------------------------------------------

    private static readonly string[] DocKeys = [.. Map(new MediaHistoryDoc()).Keys];
    private static readonly string[] MediumKeys = [.. Map(new MediumHistory()).Keys];
    private static readonly string[] FileKeys = [.. Map(new MediumFileActivity()).Keys];
    private static readonly string[] TransferKeys = [.. Map(new MediaTransferEntry()).Keys];
    private static readonly string[] PresentKeys = ["path", "sha256"];
    private static readonly string[] WindowKeys = ["end_utc", "start_utc"];
    private static readonly string[] IndicatorKeys = ["detail", "id", "status", "title"];

    /// <summary>
    /// Reads an export. Only the canonical form is accepted (the bytes must be exactly what <see cref="Canonical"/> writes for the content), so what the
    /// officer signed is what is read; throws <see cref="FormatException"/> with the reason otherwise.
    /// </summary>
    public static MediaHistoryDoc Parse(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) throw new FormatException("fișierul începe cu BOM; forma canonică este UTF-8 fără BOM");
        if (bytes.Length > 64 * 1024 * 1024) throw new FormatException("fișierul este prea mare pentru un export al istoricului mediilor");
        MediaHistoryDoc doc;
        try
        {
            using var j = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var r = Obj(j.RootElement, DocKeys, "export");
            doc = new MediaHistoryDoc
            {
                AnchorCode = Str(r, "anchor_code"), AnchorSeq = Num(r, "anchor_seq"), Edition = Str(r, "edition"), ExportedUtc = Str(r, "exported_utc"), FileAuditing = Str(r, "file_auditing"),
                Format = Str(r, "format"), HistoryFromUtc = Str(r, "history_from_utc"), Posture = Str(r, "posture"), RegisterSha256 = Str(r, "register_sha256"),
                RegisterVersion = Num(r, "register_version"), StationId = Str(r, "station_id"), StationName = Str(r, "station_name"), TimeZone = Str(r, "time_zone"),
                Notes = [.. Arr(r, "notes").Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : throw new FormatException("o notă nu este text"))],
                Indicators = [.. Arr(r, "indicators").Select(e => Obj(e, IndicatorKeys, "indicator")).Select(e => new StationIndicator { Detail = Str(e, "detail"), Id = Str(e, "id"), Status = Str(e, "status"), Title = Str(e, "title") })],
                PresentFiles = [.. Arr(r, "present_files").Select(e => Obj(e, PresentKeys, "fișier prezent")).Select(e => new PresentFile { Path = Str(e, "path"), Sha256 = Str(e, "sha256") })],
                Transfers = [.. Arr(r, "transfers").Select(e => Obj(e, TransferKeys, "transfer")).Select(e => new MediaTransferEntry
                {
                    Approver = Str(e, "approver"), Content = Str(e, "content"), DateTime = Str(e, "date_time"), DestinationStation = Str(e, "destination_station"), Direction = Str(e, "direction"),
                    MediumRegistration = Str(e, "medium_registration"), MediumSerial = Str(e, "medium_serial"), Person = Str(e, "person"), RegistrationNumber = Str(e, "registration_number"),
                    Sha256 = Str(e, "sha256"), SourceStation = Str(e, "source_station"),
                })],
                UnattributedFiles = [.. Arr(r, "unattributed_files").Select(FileOf)],
                Media = [.. Arr(r, "media").Select(e => Obj(e, MediumKeys, "mediu")).Select(e => new MediumHistory
                {
                    BitLockerVolumeId = Str(e, "bitlocker_volume_id"), CapacityBytes = Num(e, "capacity_bytes"),
                    ConnectedNow = e.GetProperty("connected_now").ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new FormatException("„connected_now” nu este true/false") },
                    Connections = [.. Arr(e, "connections").Select(w => Obj(w, WindowKeys, "conectare")).Select(w => new MediumConnectionWindow { StartUtc = Str(w, "start_utc"), EndUtc = Str(w, "end_utc") })],
                    Description = Str(e, "description"), DriveLetters = Str(e, "drive_letters"), Files = [.. Arr(e, "files").Select(FileOf)],
                    FirstConnectedUtc = Str(e, "first_connected_utc"), LastConnectedUtc = Str(e, "last_connected_utc"), Serial = Str(e, "serial"), VidPid = Str(e, "vid_pid"),
                    VolumeSerials = [.. Arr(e, "volume_serials").Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : throw new FormatException("o serie de volum nu este text"))],
                })],
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new FormatException("exportul nu poate fi citit: " + ex.Message, ex);
        }
        if (!Canonical(doc).AsSpan().SequenceEqual(bytes)) throw new FormatException("fișierul nu este în forma canonică (chei sortate, fără spații, UTF-8 fără BOM): a fost modificat după export");
        return doc;
    }

    private static MediumFileActivity FileOf(JsonElement e)
    {
        var o = Obj(e, FileKeys, "activitate pe fișier");
        return new MediumFileActivity { Direction = Str(o, "direction"), File = Str(o, "file"), Sha256 = Str(o, "sha256"), Source = Str(o, "source"), TimeUtc = Str(o, "time_utc"), Verdict = Str(o, "verdict") };
    }

    private static JsonElement Obj(JsonElement o, string[] expected, string what)
    {
        if (o.ValueKind != JsonValueKind.Object) throw new FormatException($"{what}: nu este un obiect JSON");
        var names = o.EnumerateObject().Select(p => p.Name).ToList();
        var extra = names.Except(expected, StringComparer.Ordinal).ToList();
        var missing = expected.Except(names, StringComparer.Ordinal).ToList();
        if (extra.Count > 0 || missing.Count > 0 || names.Count != names.Distinct(StringComparer.Ordinal).Count())
            throw new FormatException($"{what}: " + string.Join("; ", new[] { extra.Count > 0 ? "chei necunoscute " + string.Join(", ", extra) : "", missing.Count > 0 ? "chei lipsă " + string.Join(", ", missing) : "",
                                                                         names.Count != names.Distinct(StringComparer.Ordinal).Count() ? "chei repetate" : "" }.Where(s => s.Length > 0)));
        return o;
    }

    private static IEnumerable<JsonElement> Arr(JsonElement o, string name) =>
        o.GetProperty(name) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : throw new FormatException($"„{name}” nu este o listă");

    private static string Str(JsonElement o, string name) =>
        o.GetProperty(name) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : throw new FormatException($"„{name}” nu este text");

    private static long Num(JsonElement o, string name) =>
        o.GetProperty(name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : throw new FormatException($"„{name}” nu este un număr întreg");

    /// <summary>What makes the export unusable (empty = valid).</summary>
    public static List<string> Problems(MediaHistoryDoc d)
    {
        var p = new List<string>();
        if (d.Format != FormatId) p.Add($"formatul „{d.Format}” nu este {FormatId}");
        if (d.StationId.Trim().Length == 0 && d.StationName.Trim().Length == 0) p.Add("stația nu este numită");
        if (d.Exported is null) p.Add($"data exportului „{d.ExportedUtc}” nu are forma yyyy-MM-ddTHH:mm:ssZ");
        if (d.RegisterVersion < 0) p.Add("versiunea registrului nu poate fi negativă");
        return p;
    }
}
